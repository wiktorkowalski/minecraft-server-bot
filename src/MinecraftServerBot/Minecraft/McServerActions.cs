using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Configuration;

namespace MinecraftServerBot.Minecraft;

public sealed class McServerActions
{
    private static readonly Regex ListResponseRegex = new(
        @"There are (\d+) of a max of (\d+) players online:\s*(.*)$",
        RegexOptions.Compiled);

    private readonly IRconClient _rcon;
    private readonly ISlpClient _slp;
    private readonly IOptionsMonitor<ExecOptions> _execOptions;
    private readonly ILogger<McServerActions> _logger;

    public McServerActions(
        IRconClient rcon,
        ISlpClient slp,
        IOptionsMonitor<ExecOptions> execOptions,
        ILogger<McServerActions> logger)
    {
        _rcon = rcon;
        _slp = slp;
        _execOptions = execOptions;
        _logger = logger;
    }

    public Task<McServerStatus> GetStatusAsync(CancellationToken ct = default) =>
        _slp.QueryAsync(ct);

    public async Task<IReadOnlyList<string>> ListPlayersAsync(CancellationToken ct = default)
    {
        var result = await _rcon.ExecuteAsync("list", ct);
        if (!result.Ok)
        {
            return [];
        }

        var match = ListResponseRegex.Match(result.Output);
        if (!match.Success)
        {
            return [];
        }

        var players = match.Groups[3].Value;
        if (string.IsNullOrWhiteSpace(players))
        {
            return [];
        }

        return players
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    public Task<RconResult> SaveAllAsync(CancellationToken ct = default) =>
        _rcon.ExecuteAsync("save-all", ct);

    public Task<RconResult> SayAsync(string message, CancellationToken ct = default) =>
        _rcon.ExecuteAsync($"say {message}", ct);

    public async Task<RconResult> RestartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initiating restart: save-all then stop");
        var save = await _rcon.ExecuteAsync("save-all", ct);
        if (!save.Ok)
        {
            return save;
        }

        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        return await _rcon.ExecuteAsync("stop", ct);
    }

    public async Task<RconResult> ExecRawAsync(string command, CancellationToken ct = default)
    {
        var trimmed = command.TrimStart('/').Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return RconResult.Failure("empty command");
        }

        var blocklist = _execOptions.CurrentValue.Blocklist;
        foreach (var pattern in blocklist)
        {
            if (Regex.IsMatch(trimmed, pattern, RegexOptions.IgnoreCase))
            {
                _logger.LogInformation("Exec blocked by pattern {Pattern}: {Command}", pattern, trimmed);
                return RconResult.Failure($"blocked by pattern: {pattern}");
            }
        }

        return await _rcon.ExecuteAsync(trimmed, ct);
    }
}
