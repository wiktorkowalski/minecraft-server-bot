namespace MinecraftServerBot.Configuration;

public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    public string? LangfuseEndpoint { get; init; }

    public string? LangfusePublicKey { get; init; }

    public string? LangfuseSecretKey { get; init; }

    public bool Enabled => !string.IsNullOrWhiteSpace(LangfuseEndpoint)
        && !string.IsNullOrWhiteSpace(LangfusePublicKey)
        && !string.IsNullOrWhiteSpace(LangfuseSecretKey);
}
