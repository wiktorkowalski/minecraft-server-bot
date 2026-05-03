namespace MinecraftServerBot.Configuration;

public sealed class PollerOptions
{
    public const string SectionName = "Poller";

    public int IntervalSeconds { get; init; } = 10;

    public bool RconListOnPlayerCountChange { get; init; } = true;
}
