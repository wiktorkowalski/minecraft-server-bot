namespace MinecraftServerBot.Configuration;

public sealed class PresenceOptions
{
    public const string SectionName = "Presence";

    public bool Enabled { get; init; } = true;

    public string OnlineFormat { get; init; } = "Playing {online}/{max}";

    public string OfflineText { get; init; } = "Offline";
}
