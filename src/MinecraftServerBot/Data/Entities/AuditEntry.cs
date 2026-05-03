namespace MinecraftServerBot.Data.Entities;

public enum AuditSource
{
    Slash,
    LlmTool,
    Schedule,
    System,
    InGame,
}

public enum AuditOutcome
{
    Ok,
    Blocked,
    Error,
    TimedOut,
    Cancelled,
}

public sealed class AuditEntry
{
    public long Id { get; set; }

    public DateTime OccurredUtc { get; set; }

    public ulong RequesterUserId { get; set; }

    public ulong? ConfirmerUserId { get; set; }

    public AuditSource Source { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? Args { get; set; }

    public AuditOutcome Outcome { get; set; }

    public string? Detail { get; set; }
}
