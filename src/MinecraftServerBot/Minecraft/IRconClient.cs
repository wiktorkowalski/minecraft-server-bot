namespace MinecraftServerBot.Minecraft;

public interface IRconClient
{
    Task<RconResult> ExecuteAsync(string command, CancellationToken ct = default);
}

public sealed record RconResult(bool Ok, string Output, string? Error)
{
    public static RconResult Success(string output) => new(true, output, null);

    public static RconResult Failure(string error) => new(false, string.Empty, error);
}
