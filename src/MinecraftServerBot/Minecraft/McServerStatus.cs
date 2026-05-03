namespace MinecraftServerBot.Minecraft;

public sealed record McServerStatus(
    bool Online,
    int OnlinePlayers,
    int MaxPlayers,
    string Motd,
    string Version,
    int LatencyMs,
    string? Error)
{
    public static McServerStatus Offline(string reason) =>
        new(false, 0, 0, string.Empty, string.Empty, 0, reason);
}
