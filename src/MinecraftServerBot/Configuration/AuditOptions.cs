namespace MinecraftServerBot.Configuration;

public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    public int? RetentionDays { get; init; }
}
