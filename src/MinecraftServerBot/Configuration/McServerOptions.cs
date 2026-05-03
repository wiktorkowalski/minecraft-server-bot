namespace MinecraftServerBot.Configuration;

public sealed class McServerOptions
{
    public const string SectionName = "McServer";

    public required string Host { get; init; }

    public ushort RconPort { get; init; } = 25575;

    public ushort QueryPort { get; init; } = 25565;

    public required string RconPassword { get; init; }

    public int RconConnectTimeoutMs { get; init; } = 5000;

    public int RconCommandTimeoutMs { get; init; } = 5000;
}
