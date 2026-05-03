using MinecraftServerBot.Minecraft;

namespace MinecraftServerBot.Services;

public sealed record ServerStatusChangedEvent(McServerStatus Previous, McServerStatus Current);

public sealed record PlayerCountChangedEvent(int Previous, int Current, McServerStatus Status);

public sealed record PlayerJoinedEvent(string PlayerName);

public sealed record PlayerLeftEvent(string PlayerName, TimeSpan SessionDuration);
