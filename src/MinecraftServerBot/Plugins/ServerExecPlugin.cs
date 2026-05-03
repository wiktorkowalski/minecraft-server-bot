using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using MinecraftServerBot.Configuration;
using MinecraftServerBot.Data.Entities;
using MinecraftServerBot.Minecraft;
using MinecraftServerBot.Services;

namespace MinecraftServerBot.Plugins;

public sealed class ServerExecPlugin
{
    private readonly McServerActions _actions;
    private readonly RateLimitService _rateLimit;
    private readonly AuditService _audit;
    private readonly IOptionsMonitor<ExecOptions> _execOptions;
    private readonly LlmInvocationContextAccessor _context;
    private readonly ILogger<ServerExecPlugin> _logger;

    public ServerExecPlugin(
        McServerActions actions,
        RateLimitService rateLimit,
        AuditService audit,
        IOptionsMonitor<ExecOptions> execOptions,
        LlmInvocationContextAccessor context,
        ILogger<ServerExecPlugin> logger)
    {
        _actions = actions;
        _rateLimit = rateLimit;
        _audit = audit;
        _execOptions = execOptions;
        _context = context;
        _logger = logger;
    }

    [KernelFunction("exec_rcon")]
    [Description("Run a raw RCON command on the Minecraft server. ADMIN ONLY — only the configured admin Discord user IDs can trigger this. Subject to a blocklist (op/deop/ban/pardon/whitelist/stop are refused) and a per-user rate limit. Useful for: kick <player> [reason], give <player> <item> [count], tp <target> <dest>, gamemode <mode> <player>, time set <day|night>, weather <clear|rain|thunder>, and any other safe in-game command. Output is returned verbatim, capped to a few hundred chars.")]
    public async Task<string> ExecAsync(
        [Description("The raw RCON command to run, e.g. 'kick PlayerName griefing' or 'time set day'")] string command)
    {
        var ctx = _context.Current;
        if (ctx is null)
        {
            return "can't run: no channel context.";
        }

        if (!ctx.IsAdmin)
        {
            await _audit.WriteAsync(
                ctx.UserId,
                AuditSource.LlmTool,
                "exec",
                AuditOutcome.Blocked,
                args: command,
                detail: "non-admin");
            return "you're not an admin. only configured admin discord IDs can run raw rcon. if you are an admin, try the `/exec` slash command directly.";
        }

        var perMinute = _execOptions.CurrentValue.RateLimitPerMinute;
        if (!_rateLimit.TryAcquire(ctx.UserId, perMinute, TimeSpan.FromMinutes(1)))
        {
            await _audit.WriteAsync(
                ctx.UserId,
                AuditSource.LlmTool,
                "exec",
                AuditOutcome.Blocked,
                args: command,
                detail: "rate-limited");
            return $"rate-limited ({perMinute}/min). try again in a minute.";
        }

        var result = await _actions.ExecRawAsync(command);
        if (!result.Ok)
        {
            await _audit.WriteAsync(
                ctx.UserId,
                AuditSource.LlmTool,
                "exec",
                AuditOutcome.Blocked,
                args: command,
                detail: result.Error);
            _logger.LogInformation("LLM exec refused: {Command} — {Error}", command, result.Error);
            return $"refused/failed: {result.Error}";
        }

        await _audit.WriteAsync(
            ctx.UserId,
            AuditSource.LlmTool,
            "exec",
            AuditOutcome.Ok,
            args: command);

        var output = result.Output.Length == 0 ? "(no output)" : result.Output;
        const int max = 800;
        return output.Length <= max ? output : output[..max] + "\n… (truncated)";
    }
}
