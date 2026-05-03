using System.ComponentModel;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using MinecraftServerBot.Data.Entities;
using MinecraftServerBot.Minecraft;
using MinecraftServerBot.Services;

namespace MinecraftServerBot.Plugins;

public sealed class ServerControlPlugin
{
    private readonly McServerActions _actions;
    private readonly ConfirmationService _confirmation;
    private readonly DiscordBotService _bot;
    private readonly AuditService _audit;
    private readonly LlmInvocationContextAccessor _context;
    private readonly ILogger<ServerControlPlugin> _logger;

    public ServerControlPlugin(
        McServerActions actions,
        ConfirmationService confirmation,
        DiscordBotService bot,
        AuditService audit,
        LlmInvocationContextAccessor context,
        ILogger<ServerControlPlugin> logger)
    {
        _actions = actions;
        _confirmation = confirmation;
        _bot = bot;
        _audit = audit;
        _context = context;
        _logger = logger;
    }

    [KernelFunction("broadcast")]
    [Description("Send an in-game broadcast to all players (RCON 'say <text>'). No confirmation required.")]
    public async Task<string> BroadcastAsync(
        [Description("Message text to broadcast in-game")] string message)
    {
        var ctx = _context.Current;
        var result = await _actions.SayAsync(message);
        if (ctx is not null)
        {
            await _audit.WriteAsync(
                ctx.UserId,
                AuditSource.LlmTool,
                "broadcast",
                result.Ok ? AuditOutcome.Ok : AuditOutcome.Error,
                args: message,
                detail: result.Error);
        }

        return result.Ok ? $"broadcast sent: {message}" : $"failed: {result.Error}";
    }

    [KernelFunction("save_world")]
    [Description("Stage a world save (RCON 'save-all'). This posts a Discord confirmation button — anyone in the channel can click Confirm to actually run it. Returns immediately with a status message; nothing runs until someone confirms.")]
    public Task<string> SaveAsync() =>
        StageConfirmAsync(
            "save",
            null,
            async ct =>
            {
                var r = await _actions.SaveAllAsync(ct);
                return r.Ok ? "save-all done." : $"failed: {r.Error}";
            });

    [KernelFunction("restart_server")]
    [Description("Stage a server restart (RCON 'save-all' then 'stop'; the k8s/Argo pod will come back up). Posts a Discord confirmation button — anyone in the channel can click Confirm to actually run it. Returns immediately with a status message; nothing runs until someone confirms.")]
    public Task<string> RestartAsync() =>
        StageConfirmAsync(
            "restart",
            null,
            async ct =>
            {
                var r = await _actions.RestartAsync(ct);
                return r.Ok
                    ? "restart issued: save-all + stop sent. server should come back via k8s."
                    : $"failed: {r.Error}";
            });

    private async Task<string> StageConfirmAsync(
        string action,
        string? args,
        Func<CancellationToken, Task<string>> execute)
    {
        var ctx = _context.Current;
        if (ctx is null)
        {
            _logger.LogWarning("LLM tool {Action} called with no invocation context", action);
            return $"can't stage {action}: no channel context.";
        }

        var client = _bot.Client;
        if (client is null)
        {
            return $"can't stage {action}: discord client not ready.";
        }

        try
        {
            var channel = await client.GetChannelAsync(ctx.ChannelId);
            var (embed, components, _) = _confirmation.Build(
                new ConfirmationRequest(action, args, ctx.UserId, execute));
            var builder = new DiscordMessageBuilder().AddEmbed(embed);
            builder.AddComponents(components);
            await channel.SendMessageAsync(builder);

            return $"posted a {action} confirm button in this thread — click Confirm to actually run it. expires in 60s.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stage {Action} confirmation", action);
            return $"failed to stage {action}: {ex.Message}";
        }
    }
}
