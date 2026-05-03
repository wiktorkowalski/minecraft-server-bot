using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Configuration;
using MinecraftServerBot.Data;
using MinecraftServerBot.Data.Entities;
using MinecraftServerBot.Minecraft;

namespace MinecraftServerBot.Services;

public sealed class McStatusPollerService : BackgroundService
{
    private readonly McServerActions _actions;
    private readonly IDbContextFactory<McBotDbContext> _dbFactory;
    private readonly IOptionsMonitor<PollerOptions> _options;
    private readonly ILogger<McStatusPollerService> _logger;

    private McServerStatus _lastStatus = McServerStatus.Offline("not yet polled");
    private HashSet<string> _lastPlayers = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _lastSuccessfulPollUtc;
    private string? _lastError;

    public McStatusPollerService(
        McServerActions actions,
        IDbContextFactory<McBotDbContext> dbFactory,
        IOptionsMonitor<PollerOptions> options,
        ILogger<McStatusPollerService> logger)
    {
        _actions = actions;
        _dbFactory = dbFactory;
        _options = options;
        _logger = logger;
    }

    public event Func<ServerStatusChangedEvent, Task>? StatusChanged;

    public event Func<PlayerCountChangedEvent, Task>? PlayerCountChanged;

    public event Func<PlayerJoinedEvent, Task>? PlayerJoined;

    public event Func<PlayerLeftEvent, Task>? PlayerLeft;

    public McServerStatus LastStatus => _lastStatus;

    public DateTime? LastSuccessfulPollUtc => _lastSuccessfulPollUtc;

    public string? LastError => _lastError;

    public IReadOnlyCollection<string> LastPlayers => _lastPlayers;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("McStatusPollerService starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(2, _options.CurrentValue.IntervalSeconds));
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Poller tick failed");
                _lastError = ex.Message;
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("McStatusPollerService stopping");
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var status = await _actions.GetStatusAsync(ct);
        var previous = _lastStatus;
        _lastStatus = status;

        if (status.Online)
        {
            _lastSuccessfulPollUtc = DateTime.UtcNow;
            _lastError = null;
        }
        else
        {
            _lastError = status.Error;
        }

        if (status.Online != previous.Online)
        {
            _logger.LogInformation(
                "Server status transition: {Previous} -> {Current}",
                previous.Online ? "online" : "offline",
                status.Online ? "online" : "offline");
            await SafeInvokeAsync(StatusChanged, new ServerStatusChangedEvent(previous, status));
        }

        if (status.OnlinePlayers != previous.OnlinePlayers)
        {
            await SafeInvokeAsync(PlayerCountChanged, new PlayerCountChangedEvent(previous.OnlinePlayers, status.OnlinePlayers, status));
        }

        var shouldListPlayers = status.Online
            && status.OnlinePlayers != previous.OnlinePlayers
            && _options.CurrentValue.RconListOnPlayerCountChange;

        if (shouldListPlayers)
        {
            await DiffPlayersAsync(ct);
        }
        else if (!status.Online && _lastPlayers.Count > 0)
        {
            await CloseAllSessionsAsync(ct, reason: "server offline");
            _lastPlayers = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task DiffPlayersAsync(CancellationToken ct)
    {
        var current = await _actions.ListPlayersAsync(ct);
        var currentSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);

        var joined = currentSet.Except(_lastPlayers).ToList();
        var left = _lastPlayers.Except(currentSet).ToList();

        foreach (var name in joined)
        {
            await OpenSessionAsync(name, ct);
            await SafeInvokeAsync(PlayerJoined, new PlayerJoinedEvent(name));
        }

        foreach (var name in left)
        {
            var duration = await CloseSessionAsync(name, ct);
            await SafeInvokeAsync(PlayerLeft, new PlayerLeftEvent(name, duration));
        }

        _lastPlayers = currentSet;
    }

    private async Task OpenSessionAsync(string name, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.PlayerSessions.Add(new PlayerSession
        {
            PlayerName = name,
            JoinedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task<TimeSpan> CloseSessionAsync(string name, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var open = await db.PlayerSessions
            .Where(s => s.PlayerName == name && s.LeftUtc == null)
            .OrderByDescending(s => s.JoinedUtc)
            .FirstOrDefaultAsync(ct);

        if (open is null)
        {
            return TimeSpan.Zero;
        }

        open.LeftUtc = DateTime.UtcNow;
        var duration = open.LeftUtc.Value - open.JoinedUtc;
        open.DurationSeconds = (long)duration.TotalSeconds;
        await db.SaveChangesAsync(ct);
        return duration;
    }

    private async Task CloseAllSessionsAsync(CancellationToken ct, string reason)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var open = await db.PlayerSessions.Where(s => s.LeftUtc == null).ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var session in open)
        {
            session.LeftUtc = now;
            session.DurationSeconds = (long)(now - session.JoinedUtc).TotalSeconds;
        }

        if (open.Count > 0)
        {
            _logger.LogInformation("Closing {Count} open player sessions ({Reason})", open.Count, reason);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task SafeInvokeAsync<TEvent>(Func<TEvent, Task>? handlers, TEvent evt)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<TEvent, Task>>())
        {
            try
            {
                await handler(evt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Poller event handler {Handler} threw", handler.Method.Name);
            }
        }
    }
}
