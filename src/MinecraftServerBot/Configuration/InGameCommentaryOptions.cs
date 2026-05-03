namespace MinecraftServerBot.Configuration;

public sealed class InGameCommentaryOptions
{
    public const string SectionName = "InGameCommentary";

    public bool Enabled { get; init; }

    public bool OnPlayerJoin { get; init; } = true;

    public bool OnPlayerLeave { get; init; } = true;

    public bool OnServerUpDown { get; init; } = true;

    public int PeriodicMinutes { get; init; }

    public int CooldownSeconds { get; init; } = 30;

    public int MaxLineLength { get; init; } = 120;
}
