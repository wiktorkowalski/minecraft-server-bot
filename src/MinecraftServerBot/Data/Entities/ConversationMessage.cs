namespace MinecraftServerBot.Data.Entities;

public enum ConversationRole
{
    User,
    Assistant,
    System,
    Tool,
}

public sealed class ConversationMessage
{
    public long Id { get; set; }

    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }

    public ConversationRole Role { get; set; }

    public ulong? AuthorId { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? ToolName { get; set; }

    public DateTime CreatedUtc { get; set; }
}
