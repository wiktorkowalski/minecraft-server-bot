using DSharpPlus.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Configuration;

namespace MinecraftServerBot.Services;

public sealed class PresenceUpdaterService : IHostedService
{
    private readonly DiscordBotService _bot;
    private readonly McStatusPollerService _poller;
    private readonly IOptionsMonitor<PresenceOptions> _options;
    private readonly ILogger<PresenceUpdaterService> _logger;

    public PresenceUpdaterService(
        DiscordBotService bot,
        McStatusPollerService poller,
        IOptionsMonitor<PresenceOptions> options,
        ILogger<PresenceUpdaterService> logger)
    {
        _bot = bot;
        _poller = poller;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _poller.StatusChanged += OnStatusChangedAsync;
        _poller.PlayerCountChanged += OnPlayerCountChangedAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _poller.StatusChanged -= OnStatusChangedAsync;
        _poller.PlayerCountChanged -= OnPlayerCountChangedAsync;
        return Task.CompletedTask;
    }

    private Task OnStatusChangedAsync(ServerStatusChangedEvent evt) => UpdatePresenceAsync(evt.Current);

    private Task OnPlayerCountChangedAsync(PlayerCountChangedEvent evt) => UpdatePresenceAsync(evt.Status);

    private async Task UpdatePresenceAsync(MinecraftServerBot.Minecraft.McServerStatus status)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled)
        {
            return;
        }

        var client = _bot.Client;
        if (client is null || !_bot.IsReady)
        {
            return;
        }

        try
        {
            if (status.Online)
            {
                var text = opts.OnlineFormat
                    .Replace("{online}", status.OnlinePlayers.ToString())
                    .Replace("{max}", status.MaxPlayers.ToString());
                await client.UpdateStatusAsync(new DiscordActivity(text, ActivityType.Playing), UserStatus.Online);
            }
            else
            {
                await client.UpdateStatusAsync(new DiscordActivity(opts.OfflineText, ActivityType.Playing), UserStatus.DoNotDisturb);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update Discord presence");
        }
    }
}
