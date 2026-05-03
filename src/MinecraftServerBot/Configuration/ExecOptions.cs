namespace MinecraftServerBot.Configuration;

public sealed class ExecOptions
{
    public const string SectionName = "Exec";

    public List<string> Blocklist { get; init; } =
    [
        @"^op\b",
        @"^deop\b",
        @"^ban",
        @"^pardon",
        @"^whitelist",
        @"^stop\b",
    ];

    public int OutputCharLimit { get; init; } = 1900;

    public int RateLimitPerMinute { get; init; } = 5;
}
