namespace MinecraftServerBot.Configuration;

public sealed class HealthOptions
{
    public const string SectionName = "Health";

    public bool StrictMode { get; init; } = false;
}
