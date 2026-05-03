namespace MinecraftServerBot.Plugins;

public enum InvocationOrigin
{
    Discord,
    InGame,
}

public sealed record LlmInvocationContext(
    ulong ChannelId,
    ulong GuildId,
    ulong UserId,
    bool IsAdmin,
    InvocationOrigin Origin);

public sealed class LlmInvocationContextAccessor
{
    private static readonly AsyncLocal<LlmInvocationContext?> Slot = new();

    public LlmInvocationContext? Current
    {
        get => Slot.Value;
        set => Slot.Value = value;
    }
}
