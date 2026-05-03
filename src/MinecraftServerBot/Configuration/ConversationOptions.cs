namespace MinecraftServerBot.Configuration;

public sealed class ConversationOptions
{
    public const string SectionName = "Conversation";

    public int MaxMessages { get; init; } = 100;

    public int MaxAgeDays { get; init; } = 14;

    public string SystemPrompt { get; init; } =
        "You are MinecraftServerBot, a helpful operator for a Minecraft server. " +
        "You can read live server status and run actions via tool calls. " +
        "For destructive actions (restart, save, exec), call the tool — the host will ask the user to confirm before anything runs. " +
        "Be concise. Prefer plain text replies suitable for a Discord channel.";
}
