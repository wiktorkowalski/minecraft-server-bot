namespace MinecraftServerBot.Data.Entities;

public sealed class PlayerSession
{
    public long Id { get; set; }

    public string PlayerName { get; set; } = string.Empty;

    public DateTime JoinedUtc { get; set; }

    public DateTime? LeftUtc { get; set; }

    public long? DurationSeconds { get; set; }
}
