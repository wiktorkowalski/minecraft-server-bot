namespace MinecraftServerBot.Configuration;

public sealed class McChatOptions
{
    public const string SectionName = "McChat";

    public string[] Triggers { get; init; } = ["@bot", "@mcbot"];

    public int PerPlayerPerMinute { get; init; } = 3;

    public int GlobalPerMinute { get; init; } = 20;

    public int MaxLineLength { get; init; } = 200;

    public bool AuditUntriggered { get; init; }
}
