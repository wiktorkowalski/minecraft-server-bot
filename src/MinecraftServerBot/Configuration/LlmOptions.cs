namespace MinecraftServerBot.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string Endpoint { get; init; } = "https://openrouter.ai/api/v1";

    public required string ApiKey { get; init; }

    public string Model { get; init; } = "moonshotai/kimi-k2.6";

    public double Temperature { get; init; } = 0.7;

    public int MaxOutputTokens { get; init; } = 1024;
}
