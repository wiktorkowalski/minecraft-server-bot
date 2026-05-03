using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Configuration;
using MinecraftServerBot.Data.Entities;

namespace MinecraftServerBot.Services;

public sealed record ConfirmationRequest(
    string Action,
    string? Args,
    ulong RequesterUserId,
    Func<CancellationToken, Task<string>> Execute);

internal sealed record PendingConfirmation(ConfirmationRequest Request, DateTime CreatedUtc);

public sealed class ConfirmationService
{
    private const string ConfirmPrefix = "mcbot:confirm:";
    private const string CancelPrefix = "mcbot:cancel:";

    private readonly ConcurrentDictionary<string, PendingConfirmation> _pending = new();
    private readonly IOptionsMonitor<ConfirmationOptions> _options;
    private readonly AuditService _audit;
    private readonly ILogger<ConfirmationService> _logger;

    public ConfirmationService(
        IOptionsMonitor<ConfirmationOptions> options,
        AuditService audit,
        ILogger<ConfirmationService> logger)
    {
        _options = options;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Builds a Discord embed + buttons for the proposed action and registers the pending confirmation.
    /// Caller should send the resulting message; clicks land in <see cref="HandleInteractionAsync"/>.
    /// </summary>
    public (DiscordEmbed Embed, IEnumerable<DiscordComponent> Components, string Token) Build(ConfirmationRequest request)
    {
        var token = Guid.NewGuid().ToString("N")[..16];
        var pending = new PendingConfirmation(request, DateTime.UtcNow);
        _pending[token] = pending;

        var embed = new DiscordEmbedBuilder()
            .WithTitle("Confirm action")
            .WithColor(DiscordColor.Orange)
            .AddField("Action", request.Action, true)
            .AddField("Requested by", $"<@{request.RequesterUserId}>", true);

        if (!string.IsNullOrEmpty(request.Args))
        {
            embed.AddField("Args", $"`{request.Args}`", false);
        }

        embed.WithFooter($"Times out in {_options.CurrentValue.TimeoutSeconds}s — anyone in the channel may confirm");

        var components = new DiscordComponent[]
        {
            new DiscordButtonComponent(ButtonStyle.Success, ConfirmPrefix + token, "Confirm"),
            new DiscordButtonComponent(ButtonStyle.Secondary, CancelPrefix + token, "Cancel"),
        };

        _ = ScheduleTimeoutAsync(token);

        return (embed.Build(), components, token);
    }

    public async Task<bool> HandleInteractionAsync(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        var customId = e.Interaction.Data.CustomId;

        if (customId.StartsWith(ConfirmPrefix))
        {
            var token = customId[ConfirmPrefix.Length..];
            await HandleConfirmAsync(e, token);
            return true;
        }

        if (customId.StartsWith(CancelPrefix))
        {
            var token = customId[CancelPrefix.Length..];
            await HandleCancelAsync(e, token);
            return true;
        }

        return false;
    }

    private async Task HandleConfirmAsync(ComponentInteractionCreateEventArgs e, string token)
    {
        if (!_pending.TryRemove(token, out var pending))
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("This confirmation has expired or already been resolved.")
                    .AsEphemeral());
            return;
        }

        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

        try
        {
            var result = await pending.Request.Execute(CancellationToken.None);
            await _audit.WriteAsync(
                pending.Request.RequesterUserId,
                AuditSource.LlmTool,
                pending.Request.Action,
                AuditOutcome.Ok,
                args: pending.Request.Args,
                detail: result,
                confirmerUserId: e.User.Id);

            var done = new DiscordEmbedBuilder()
                .WithTitle("✅ Action executed")
                .WithColor(DiscordColor.Green)
                .AddField("Action", pending.Request.Action, true)
                .AddField("Requester", $"<@{pending.Request.RequesterUserId}>", true)
                .AddField("Confirmed by", $"<@{e.User.Id}>", true)
                .AddField("Result", string.IsNullOrEmpty(result) ? "(no output)" : result, false)
                .Build();

            await e.Interaction.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().AddEmbed(done).AddComponents(Array.Empty<DiscordComponent>()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Confirmed action {Action} failed", pending.Request.Action);
            await _audit.WriteAsync(
                pending.Request.RequesterUserId,
                AuditSource.LlmTool,
                pending.Request.Action,
                AuditOutcome.Error,
                args: pending.Request.Args,
                detail: ex.Message,
                confirmerUserId: e.User.Id);

            var fail = new DiscordEmbedBuilder()
                .WithTitle("❌ Action failed")
                .WithColor(DiscordColor.Red)
                .AddField("Action", pending.Request.Action, true)
                .AddField("Error", ex.Message, false)
                .Build();

            await e.Interaction.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().AddEmbed(fail).AddComponents(Array.Empty<DiscordComponent>()));
        }
    }

    private async Task HandleCancelAsync(ComponentInteractionCreateEventArgs e, string token)
    {
        if (!_pending.TryRemove(token, out var pending))
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("This confirmation has expired or already been resolved.")
                    .AsEphemeral());
            return;
        }

        await _audit.WriteAsync(
            pending.Request.RequesterUserId,
            AuditSource.LlmTool,
            pending.Request.Action,
            AuditOutcome.Cancelled,
            args: pending.Request.Args,
            confirmerUserId: e.User.Id);

        var cancelled = new DiscordEmbedBuilder()
            .WithTitle("⚪ Cancelled")
            .WithColor(DiscordColor.Gray)
            .AddField("Action", pending.Request.Action, true)
            .AddField("Cancelled by", $"<@{e.User.Id}>", true)
            .Build();

        await e.Interaction.CreateResponseAsync(
            InteractionResponseType.UpdateMessage,
            new DiscordInteractionResponseBuilder().AddEmbed(cancelled).AddComponents(Array.Empty<DiscordComponent>()));
    }

    private async Task ScheduleTimeoutAsync(string token)
    {
        await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.TimeoutSeconds));
        if (_pending.TryRemove(token, out var pending))
        {
            await _audit.WriteAsync(
                pending.Request.RequesterUserId,
                AuditSource.LlmTool,
                pending.Request.Action,
                AuditOutcome.TimedOut,
                args: pending.Request.Args);
            _logger.LogInformation("Confirmation timed out: {Action} (requester {User})",
                pending.Request.Action, pending.Request.RequesterUserId);
        }
    }
}
