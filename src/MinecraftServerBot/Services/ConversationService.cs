using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using MinecraftServerBot.Configuration;
using MinecraftServerBot.Data;
using MinecraftServerBot.Data.Entities;

namespace MinecraftServerBot.Services;

public sealed class ConversationService
{
    private readonly IDbContextFactory<McBotDbContext> _dbFactory;
    private readonly IOptionsMonitor<ConversationOptions> _options;

    public ConversationService(
        IDbContextFactory<McBotDbContext> dbFactory,
        IOptionsMonitor<ConversationOptions> options)
    {
        _dbFactory = dbFactory;
        _options = options;
    }

    public async Task<ChatHistory> LoadHistoryAsync(ulong guildId, ulong channelId, CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var cutoff = DateTime.UtcNow.AddDays(-opts.MaxAgeDays);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var rows = await db.ConversationMessages
            .Where(m => m.ChannelId == channelId && m.CreatedUtc >= cutoff)
            .OrderByDescending(m => m.CreatedUtc)
            .Take(opts.MaxMessages)
            .ToListAsync(ct);

        rows.Reverse();

        var history = new ChatHistory(opts.SystemPrompt);
        foreach (var row in rows)
        {
            switch (row.Role)
            {
                case ConversationRole.User:
                    history.AddUserMessage(row.Content);
                    break;
                case ConversationRole.Assistant:
                    history.AddAssistantMessage(row.Content);
                    break;
                case ConversationRole.Tool:
                    history.AddMessage(AuthorRole.Tool, row.Content);
                    break;
                case ConversationRole.System:
                    break;
            }
        }

        return history;
    }

    public async Task PersistAsync(
        ulong guildId,
        ulong channelId,
        ConversationRole role,
        string content,
        ulong? authorId = null,
        string? toolName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.ConversationMessages.Add(new ConversationMessage
        {
            GuildId = guildId,
            ChannelId = channelId,
            Role = role,
            Content = content,
            AuthorId = authorId,
            ToolName = toolName,
            CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
