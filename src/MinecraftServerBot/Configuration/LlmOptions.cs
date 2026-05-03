namespace MinecraftServerBot.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string Endpoint { get; init; } = "https://openrouter.ai/api/v1";

    public required string ApiKey { get; init; }

    public string Model { get; init; } = "deepseek/deepseek-v4-flash";

    public double Temperature { get; init; } = 0.3;

    public int MaxOutputTokens { get; init; } = 1024;
}
