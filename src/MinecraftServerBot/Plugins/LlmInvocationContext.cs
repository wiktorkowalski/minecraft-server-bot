namespace MinecraftServerBot.Plugins;

public sealed record LlmInvocationContext(ulong ChannelId, ulong GuildId, ulong UserId, bool IsAdmin);

public sealed class LlmInvocationContextAccessor
{
    private static readonly AsyncLocal<LlmInvocationContext?> Slot = new();

    public LlmInvocationContext? Current
    {
        get => Slot.Value;
        set => Slot.Value = value;
    }
}
