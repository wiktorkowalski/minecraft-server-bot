namespace MinecraftServerBot.Configuration;

public sealed class ConfirmationOptions
{
    public const string SectionName = "Confirmation";

    public int TimeoutSeconds { get; init; } = 60;
}
