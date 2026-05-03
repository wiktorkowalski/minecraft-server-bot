namespace MinecraftServerBot.Configuration;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public required string Token { get; init; }

    public required ulong GuildId { get; init; }

    public required ulong AllowedChannelId { get; init; }

    public List<ulong> AdminUserIds { get; init; } = [];

    public bool DmFromAdminsOnly { get; init; } = true;

    public bool PublicActionConfirmations { get; init; }
}
