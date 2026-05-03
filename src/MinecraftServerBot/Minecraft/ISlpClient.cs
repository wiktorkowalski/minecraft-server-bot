namespace MinecraftServerBot.Minecraft;

public interface ISlpClient
{
    Task<McServerStatus> QueryAsync(CancellationToken ct = default);
}
