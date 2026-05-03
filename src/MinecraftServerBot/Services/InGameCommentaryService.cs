using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Configuration;
using MinecraftServerBot.Minecraft;

namespace MinecraftServerBot.Services;

public sealed class InGameCommentaryService : BackgroundService
{
    private readonly McStatusPollerService _poller;
    private readonly McServerActions _actions;
    private readonly KernelService _kernel;
    private readonly IOptionsMonitor<InGameCommentaryOptions> _options;
    private readonly IOptionsMonitor<ConversationOptions> _convo;
    private readonly ILogger<InGameCommentaryService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastSayUtc = DateTime.MinValue;

    public InGameCommentaryService(
        McStatusPollerService poller,
        McServerActions actions,
        KernelService kernel,
        IOptionsMonitor<InGameCommentaryOptions> options,
        IOptionsMonitor<ConversationOptions> convo,
        ILogger<InGameCommentaryService> logger)
    {
        _poller = poller;
        _actions = actions;
        _kernel = kernel;
        _options = options;
        _convo = convo;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _poller.StatusChanged += OnStatusChangedAsync;
        _poller.PlayerJoined += OnPlayerJoinedAsync;
        _poller.PlayerLeft += OnPlayerLeftAsync;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var minutes = _options.CurrentValue.PeriodicMinutes;
                if (minutes <= 0)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken);
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                if (!_poller.LastStatus.Online)
                {
                    continue;
                }

                await SayCommentaryAsync(
                    "you're bored. drop a random short doomer-meme one-liner in chat to amuse the players online. nothing about a specific event, just vibes.",
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        finally
        {
            _poller.StatusChanged -= OnStatusChangedAsync;
            _poller.PlayerJoined -= OnPlayerJoinedAsync;
            _poller.PlayerLeft -= OnPlayerLeftAsync;
        }
    }

    private Task OnStatusChangedAsync(ServerStatusChangedEvent evt)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled || !opts.OnServerUpDown)
        {
            return Task.CompletedTask;
        }

        if (!evt.Current.Online)
        {
            return Task.CompletedTask;
        }

        _ = SayCommentaryAsync(
            "the minecraft server just came back online after being down. greet whoever's around with a dryly funny one-liner.",
            CancellationToken.None);
        return Task.CompletedTask;
    }

    private Task OnPlayerJoinedAsync(PlayerJoinedEvent evt)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled || !opts.OnPlayerJoin)
        {
            return Task.CompletedTask;
        }

        _ = SayCommentaryAsync(
            $"player '{evt.PlayerName}' just joined the server. react with a one-liner addressed to them (you can use their name).",
            CancellationToken.None);
        return Task.CompletedTask;
    }

    private Task OnPlayerLeftAsync(PlayerLeftEvent evt)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled || !opts.OnPlayerJoin)
        {
            return Task.CompletedTask;
        }

        _ = SayCommentaryAsync(
            $"player '{evt.PlayerName}' just left the server after {(int)evt.SessionDuration.TotalMinutes} minutes. one-liner farewell, can be cynical.",
            CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task SayCommentaryAsync(string trigger, CancellationToken ct)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled)
        {
            return;
        }

        await _gate.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastSayUtc;
            if (elapsed < TimeSpan.FromSeconds(opts.CooldownSeconds))
            {
                _logger.LogDebug("Commentary cooldown active ({Elapsed}s < {Cooldown}s), skipping",
                    (int)elapsed.TotalSeconds, opts.CooldownSeconds);
                return;
            }

            var system = _convo.CurrentValue.SystemPrompt
                + "\n\nyou are NOT replying in discord. you are about to be broadcast directly into in-game minecraft chat via RCON `say`. "
                + $"output ONE single short line (max {opts.MaxLineLength} chars). "
                + "no quotes, no markdown (minecraft chat is plain text), no @mentions, no newlines. just the raw line of text.";

            var line = await _kernel.OneShotAsync(system, trigger, maxTokens: 200, ct);
            line = McChatSanitizer.OneLine(line, opts.MaxLineLength);
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var result = await _actions.TellrawAllAsync(line, ct);
            if (result.Ok)
            {
                _lastSayUtc = DateTime.UtcNow;
                _logger.LogInformation("In-game commentary: [bot] {Line}", line);
            }
            else
            {
                _logger.LogWarning("In-game commentary RCON failed: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Commentary failed: {Trigger}", trigger);
        }
        finally
        {
            _gate.Release();
        }
    }
}
