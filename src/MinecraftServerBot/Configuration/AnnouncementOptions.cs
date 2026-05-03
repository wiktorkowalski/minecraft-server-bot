namespace MinecraftServerBot.Configuration;

public sealed class AnnouncementOptions
{
    public const string SectionName = "Announcements";

    public bool Enabled { get; init; } = false;

    public bool ServerUpDown { get; init; } = false;

    public bool PlayerJoinLeave { get; init; } = false;

    public bool AdminActions { get; init; } = false;

    public ulong? MentionRoleId { get; init; }
}
