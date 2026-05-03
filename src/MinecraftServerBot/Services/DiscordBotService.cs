using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Commands;
using MinecraftServerBot.Configuration;
using MinecraftServerBot.Data.Entities;
using MinecraftServerBot.Plugins;

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
        _client.MessageCreated += OnMessageCreatedAsync;
        _client.ComponentInteractionCreated += OnComponentInteractionAsync;

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

    private async Task OnMessageCreatedAsync(DiscordClient client, MessageCreateEventArgs e)
    {
        if (e.Author.IsBot)
        {
            return;
        }

        var channel = e.Channel;
        var isDm = channel.IsPrivate;
        var mentionsUs = e.MentionedUsers.Any(u => u.Id == client.CurrentUser.Id);

        DiscordChannel replyChannel;
        var spawnNewThread = false;

        if (isDm)
        {
            if (_options.DmFromAdminsOnly && !_options.AdminUserIds.Contains(e.Author.Id))
            {
                return;
            }

            replyChannel = channel;
        }
        else if (channel.IsThread && channel.ParentId == _options.AllowedChannelId)
        {
            if (channel is DiscordThreadChannel thread && thread.CreatorId != client.CurrentUser.Id)
            {
                return;
            }

            replyChannel = channel;
        }
        else if (channel.Id == _options.AllowedChannelId && mentionsUs)
        {
            spawnNewThread = true;
            replyChannel = channel;
        }
        else
        {
            return;
        }

        try
        {
            var content = StripMention(e.Message.Content, client.CurrentUser.Id);
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            if (spawnNewThread)
            {
                replyChannel = await e.Message.CreateThreadAsync(
                    BuildThreadName(content),
                    AutoArchiveDuration.Day);
            }

            await using var scope = _services.CreateAsyncScope();
            var conversation = scope.ServiceProvider.GetRequiredService<ConversationService>();
            var kernel = scope.ServiceProvider.GetRequiredService<KernelService>();
            var ctxAccessor = scope.ServiceProvider.GetRequiredService<LlmInvocationContextAccessor>();

            await replyChannel.TriggerTypingAsync();

            var guildId = e.Guild?.Id ?? 0UL;
            var history = await conversation.LoadHistoryAsync(guildId, replyChannel.Id);
            history.AddUserMessage(content);

            var isAdmin = _options.AdminUserIds.Contains(e.Author.Id);
            ctxAccessor.Current = new LlmInvocationContext(replyChannel.Id, guildId, e.Author.Id, isAdmin);

            var response = await kernel.CompleteAsync(history);
            var text = string.IsNullOrWhiteSpace(response.Content)
                ? "ok, ogarnięte 👍"
                : response.Content;

            await conversation.PersistAsync(guildId, replyChannel.Id, ConversationRole.User, content, e.Author.Id);
            await conversation.PersistAsync(guildId, replyChannel.Id, ConversationRole.Assistant, text);

            await replyChannel.SendMessageAsync(text.Length > 1900 ? text[..1900] + "…" : text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message from {User}", e.Author.Id);
            try
            {
                await e.Channel.SendMessageAsync($"Sorry, hit an error: `{ex.Message}`");
            }
            catch
            {
                // best effort
            }
        }
    }

    private static string BuildThreadName(string content)
    {
        var oneLine = content.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (oneLine.Length > 90)
        {
            oneLine = oneLine[..90];
        }

        return string.IsNullOrWhiteSpace(oneLine) ? "Chat" : oneLine;
    }

    private async Task OnComponentInteractionAsync(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var confirmation = scope.ServiceProvider.GetRequiredService<ConfirmationService>();
            await confirmation.HandleInteractionAsync(client, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling component interaction");
        }
    }

    private static string StripMention(string content, ulong botUserId)
    {
        var mentionTags = new[] { $"<@{botUserId}>", $"<@!{botUserId}>" };
        foreach (var tag in mentionTags)
        {
            content = content.Replace(tag, string.Empty);
        }

        return content.Trim();
    }
}
