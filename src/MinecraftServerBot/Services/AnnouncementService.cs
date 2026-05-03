using DSharpPlus.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Configuration;

namespace MinecraftServerBot.Services;

public sealed class AnnouncementService : IHostedService
{
    private readonly DiscordBotService _bot;
    private readonly McStatusPollerService _poller;
    private readonly IOptionsMonitor<AnnouncementOptions> _announcementOptions;
    private readonly IOptionsMonitor<DiscordOptions> _discordOptions;
    private readonly ILogger<AnnouncementService> _logger;

    public AnnouncementService(
        DiscordBotService bot,
        McStatusPollerService poller,
        IOptionsMonitor<AnnouncementOptions> announcementOptions,
        IOptionsMonitor<DiscordOptions> discordOptions,
        ILogger<AnnouncementService> logger)
    {
        _bot = bot;
        _poller = poller;
        _announcementOptions = announcementOptions;
        _discordOptions = discordOptions;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _poller.StatusChanged += OnStatusChangedAsync;
        _poller.PlayerJoined += OnPlayerJoinedAsync;
        _poller.PlayerLeft += OnPlayerLeftAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _poller.StatusChanged -= OnStatusChangedAsync;
        _poller.PlayerJoined -= OnPlayerJoinedAsync;
        _poller.PlayerLeft -= OnPlayerLeftAsync;
        return Task.CompletedTask;
    }

    private Task OnStatusChangedAsync(ServerStatusChangedEvent evt)
    {
        var opts = _announcementOptions.CurrentValue;
        if (!opts.Enabled || !opts.ServerUpDown)
        {
            return Task.CompletedTask;
        }

        var line = evt.Current.Online
            ? "🟢 Server is back online."
            : $"🔴 Server appears offline ({evt.Current.Error}).";
        return PostAsync(line);
    }

    private Task OnPlayerJoinedAsync(PlayerJoinedEvent evt)
    {
        var opts = _announcementOptions.CurrentValue;
        if (!opts.Enabled || !opts.PlayerJoinLeave)
        {
            return Task.CompletedTask;
        }

        return PostAsync($"➡️ **{evt.PlayerName}** joined the server.");
    }

    private Task OnPlayerLeftAsync(PlayerLeftEvent evt)
    {
        var opts = _announcementOptions.CurrentValue;
        if (!opts.Enabled || !opts.PlayerJoinLeave)
        {
            return Task.CompletedTask;
        }

        var duration = FormatDuration(evt.SessionDuration);
        return PostAsync($"⬅️ **{evt.PlayerName}** left the server (session: {duration}).");
    }

    private async Task PostAsync(string content)
    {
        var client = _bot.Client;
        if (client is null || !_bot.IsReady)
        {
            return;
        }

        var channelId = _discordOptions.CurrentValue.AllowedChannelId;
        if (channelId == 0)
        {
            return;
        }

        try
        {
            var channel = await client.GetChannelAsync(channelId);
            var role = _announcementOptions.CurrentValue.MentionRoleId;
            var prefix = role is { } r and not 0 ? $"<@&{r}> " : string.Empty;
            await channel.SendMessageAsync(prefix + content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to post announcement to channel {ChannelId}", channelId);
        }
    }

    private static string FormatDuration(TimeSpan d)
    {
        if (d.TotalSeconds < 60)
        {
            return $"{(int)d.TotalSeconds}s";
        }

        if (d.TotalMinutes < 60)
        {
            return $"{(int)d.TotalMinutes}m";
        }

        return $"{(int)d.TotalHours}h{d.Minutes:D2}m";
    }
}
