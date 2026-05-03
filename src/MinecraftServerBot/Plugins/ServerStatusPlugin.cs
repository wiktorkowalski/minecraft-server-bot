using System.ComponentModel;
using Microsoft.SemanticKernel;
using MinecraftServerBot.Minecraft;

namespace MinecraftServerBot.Plugins;

public sealed class ServerStatusPlugin
{
    private readonly McServerActions _actions;

    public ServerStatusPlugin(McServerActions actions)
    {
        _actions = actions;
    }

    [KernelFunction("get_server_status")]
    [Description("Returns the current Minecraft server status: online/offline, online player count, max players, MOTD, version, and ping latency.")]
    public async Task<string> GetStatusAsync()
    {
        var status = await _actions.GetStatusAsync();
        if (!status.Online)
        {
            return $"Server is offline (reason: {status.Error}).";
        }

        return $"Server online — {status.OnlinePlayers}/{status.MaxPlayers} players. Version: {status.Version}. MOTD: {status.Motd}. Latency: {status.LatencyMs} ms.";
    }

    [KernelFunction("list_online_players")]
    [Description("Returns the list of player names currently connected to the Minecraft server.")]
    public async Task<string> ListPlayersAsync()
    {
        var players = await _actions.ListPlayersAsync();
        return players.Count == 0
            ? "No players are currently online."
            : $"Online players ({players.Count}): {string.Join(", ", players)}";
    }
}
