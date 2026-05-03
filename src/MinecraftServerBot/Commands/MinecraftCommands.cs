using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Configuration;
using MinecraftServerBot.Data.Entities;
using MinecraftServerBot.Minecraft;
using MinecraftServerBot.Services;

namespace MinecraftServerBot.Commands;

public sealed class MinecraftCommands : ApplicationCommandModule
{
    public required McServerActions Actions { get; set; }

    public required McStatusPollerService Poller { get; set; }

    public required RateLimitService RateLimit { get; set; }

    public required ConfirmationService Confirmation { get; set; }

    public required AuditService Audit { get; set; }

    public required IOptionsMonitor<DiscordOptions> DiscordOptions { get; set; }

    public required IOptionsMonitor<ExecOptions> ExecOptions { get; set; }

    public required IOptionsMonitor<McServerOptions> McOptions { get; set; }

    public required ILogger<MinecraftCommands> Logger { get; set; }

    [SlashCommand("status", "Show the current server status (online/offline, players, MOTD, version)")]
    public async Task StatusAsync(InteractionContext ctx)
    {
        await ctx.DeferAsync();
        var status = Poller.LastStatus.Online ? Poller.LastStatus : await Actions.GetStatusAsync();

        var embed = BuildStatusEmbed(status);
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    [SlashCommand("players", "List players currently online (via RCON)")]
    public async Task PlayersAsync(InteractionContext ctx)
    {
        await ctx.DeferAsync();
        var players = await Actions.ListPlayersAsync();

        var content = players.Count == 0
            ? "No players online."
            : $"**Players online ({players.Count})**: {string.Join(", ", players)}";

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(content));
    }

    [SlashCommand("motd", "Show the server MOTD")]
    public async Task MotdAsync(InteractionContext ctx)
    {
        await ctx.DeferAsync();
        var status = await Actions.GetStatusAsync();
        var content = status.Online
            ? $"```\n{status.Motd}\n```"
            : $"Server is offline ({status.Error}).";
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(content));
    }

    [SlashCommand("version", "Show the server version")]
    public async Task VersionAsync(InteractionContext ctx)
    {
        await ctx.DeferAsync();
        var status = await Actions.GetStatusAsync();
        var content = status.Online
            ? $"Version: `{status.Version}`"
            : $"Server is offline ({status.Error}).";
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(content));
    }

    [SlashCommand("say", "Broadcast a message in-game via RCON")]
    public async Task SayAsync(
        InteractionContext ctx,
        [Option("message", "Message to broadcast")] string message)
    {
        await ctx.DeferAsync();
        var result = await Actions.SayAsync(message);
        var content = result.Ok ? $"Broadcast sent: `{message}`" : $"Failed: {result.Error}";
        await Audit.WriteAsync(ctx.User.Id, AuditSource.Slash, "say",
            result.Ok ? AuditOutcome.Ok : AuditOutcome.Error, args: message, detail: result.Error);
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(content));
    }

    [SlashCommand("save", "Save the world (save-all) — requires confirmation")]
    public async Task SaveAsync(InteractionContext ctx)
    {
        await RequestConfirmationAsync(ctx, "save", null, async ct =>
        {
            var result = await Actions.SaveAllAsync(ct);
            return result.Ok ? "save-all issued." : $"Failed: {result.Error}";
        });
    }

    [SlashCommand("restart", "Restart the server (save-all + stop) — requires confirmation")]
    public async Task RestartAsync(InteractionContext ctx)
    {
        await RequestConfirmationAsync(ctx, "restart", null, async ct =>
        {
            var result = await Actions.RestartAsync(ct);
            return result.Ok
                ? "Restart initiated: save-all + stop sent. Server will come back via k8s/Argo."
                : $"Failed: {result.Error}";
        });
    }

    private async Task RequestConfirmationAsync(
        InteractionContext ctx,
        string action,
        string? args,
        Func<CancellationToken, Task<string>> execute)
    {
        await ctx.DeferAsync();
        var (embed, components, _) = Confirmation.Build(new ConfirmationRequest(action, args, ctx.User.Id, execute));
        var builder = new DiscordWebhookBuilder().AddEmbed(embed);
        builder.AddComponents(components);
        await ctx.EditResponseAsync(builder);
    }

    [SlashCommand("exec", "[Admin] Run a raw RCON command")]
    public async Task ExecAsync(
        InteractionContext ctx,
        [Option("command", "Raw RCON command (e.g. 'list', 'time set day')")] string command)
    {
        await ctx.DeferAsync();

        var admins = DiscordOptions.CurrentValue.AdminUserIds;
        if (!admins.Contains(ctx.User.Id))
        {
            await Audit.WriteAsync(ctx.User.Id, AuditSource.Slash, "exec",
                AuditOutcome.Blocked, args: command, detail: "non-admin");
            await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent("`/exec` is admin-only."));
            return;
        }

        var rateLimit = ExecOptions.CurrentValue.RateLimitPerMinute;
        if (!RateLimit.TryAcquire(ctx.User.Id, rateLimit, TimeSpan.FromMinutes(1)))
        {
            await Audit.WriteAsync(ctx.User.Id, AuditSource.Slash, "exec",
                AuditOutcome.Blocked, args: command, detail: "rate-limited");
            await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent($"Rate limit hit ({rateLimit}/min). Try again later."));
            return;
        }

        var result = await Actions.ExecRawAsync(command);
        if (!result.Ok)
        {
            await Audit.WriteAsync(ctx.User.Id, AuditSource.Slash, "exec",
                AuditOutcome.Blocked, args: command, detail: result.Error);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent($"Refused/failed: {result.Error}"));
            return;
        }

        await Audit.WriteAsync(ctx.User.Id, AuditSource.Slash, "exec",
            AuditOutcome.Ok, args: command);

        var limit = ExecOptions.CurrentValue.OutputCharLimit;
        var output = result.Output.Length == 0 ? "(no output)" : result.Output;
        if (output.Length <= limit)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent($"```\n{output}\n```"));
            return;
        }

        var truncated = output[..Math.Min(limit, output.Length)] + "\n… (truncated)";
        var bytes = System.Text.Encoding.UTF8.GetBytes(output);
        using var ms = new MemoryStream(bytes);
        await ctx.EditResponseAsync(new DiscordWebhookBuilder()
            .WithContent($"```\n{truncated}\n```")
            .AddFile("output.txt", ms));
    }

    private DiscordEmbed BuildStatusEmbed(McServerStatus status)
    {
        var mc = McOptions.CurrentValue;
        var color = status.Online ? DiscordColor.Green : DiscordColor.Red;
        var title = status.Online ? "🟢 Online" : "🔴 Offline";

        var b = new DiscordEmbedBuilder()
            .WithTitle($"{title} — {mc.Host}")
            .WithColor(color)
            .WithTimestamp(DateTimeOffset.UtcNow);

        if (status.Online)
        {
            b.AddField("Players", $"{status.OnlinePlayers}/{status.MaxPlayers}", true);
            b.AddField("Version", string.IsNullOrEmpty(status.Version) ? "?" : status.Version, true);
            b.AddField("Latency", $"{status.LatencyMs} ms", true);
            if (!string.IsNullOrWhiteSpace(status.Motd))
            {
                b.AddField("MOTD", status.Motd, false);
            }
        }
        else
        {
            b.AddField("Reason", status.Error ?? "unknown", false);
        }

        if (Poller.LastSuccessfulPollUtc is { } poll)
        {
            var age = (DateTime.UtcNow - poll).TotalSeconds;
            b.WithFooter($"Last successful poll: {age:F0}s ago");
        }

        return b.Build();
    }
}
