using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Commands;
using MinecraftServerBot.Configuration;

namespace MinecraftServerBot.Services;

public sealed class DiscordBotService : BackgroundService
{
    private const int MaxReconnectAttempts = 10;

    private readonly DiscordOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly TaskCompletionSource _readyTcs = new();

    private DiscordClient? _client;
    private SlashCommandsExtension? _slashCommands;
    private int _reconnectAttempts;

    public DiscordBotService(
        IOptions<DiscordOptions> options,
        IServiceProvider services,
        ILogger<DiscordBotService> logger)
    {
        _options = options.Value;
        _services = services;
        _logger = logger;
    }

    public DiscordClient? Client => _client;

    public bool IsReady => _readyTcs.Task.IsCompletedSuccessfully;

    public Task WaitForReadyAsync(CancellationToken ct = default) =>
        _readyTcs.Task.WaitAsync(ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DiscordBotService starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRunAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _reconnectAttempts++;
                var delay = GetBackoffDelay(_reconnectAttempts);
                _logger.LogError(
                    ex,
                    "Discord connection failed (attempt {Attempt}/{Max}). Retrying in {Delay}s",
                    _reconnectAttempts,
                    MaxReconnectAttempts,
                    delay.TotalSeconds);

                if (_reconnectAttempts >= MaxReconnectAttempts)
                {
                    _logger.LogCritical("Max reconnect attempts reached. Giving up");
                    throw;
                }

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static TimeSpan GetBackoffDelay(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(attempt, 6))));

    private async Task ConnectAndRunAsync(CancellationToken stoppingToken)
    {
        var config = new DiscordConfiguration
        {
            Token = _options.Token,
            TokenType = TokenType.Bot,
            Intents = DiscordIntents.AllUnprivileged
                | DiscordIntents.MessageContents
                | DiscordIntents.GuildMessages
                | DiscordIntents.DirectMessages,
            AutoReconnect = true,
            MinimumLogLevel = LogLevel.Warning,
        };

        _client = new DiscordClient(config);

        _slashCommands = _client.UseSlashCommands(new SlashCommandsConfiguration
        {
            Services = _services,
        });
        _slashCommands.RegisterCommands<MinecraftCommands>(_options.GuildId);

        _client.Ready += OnReady;
        _client.SocketErrored += OnSocketError;
        _client.Resumed += OnResumed;

        await _client.ConnectAsync();
        _reconnectAttempts = 0;

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private Task OnReady(DiscordClient client, ReadyEventArgs e)
    {
        _logger.LogInformation(
            "Discord connected as {Username}#{Discriminator}",
            client.CurrentUser.Username,
            client.CurrentUser.Discriminator);
        _readyTcs.TrySetResult();
        return Task.CompletedTask;
    }

    private Task OnResumed(DiscordClient client, ReadyEventArgs e)
    {
        _logger.LogInformation("Discord connection resumed");
        return Task.CompletedTask;
    }

    private Task OnSocketError(DiscordClient client, SocketErrorEventArgs e)
    {
        _logger.LogWarning(e.Exception, "Discord socket error");
        return Task.CompletedTask;
    }
}
