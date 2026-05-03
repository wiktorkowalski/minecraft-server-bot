using System.Net;
using CoreRCON;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Configuration;

namespace MinecraftServerBot.Minecraft;

public sealed class RconClient : IRconClient
{
    private readonly IOptionsMonitor<McServerOptions> _options;
    private readonly ILogger<RconClient> _logger;

    public RconClient(IOptionsMonitor<McServerOptions> options, ILogger<RconClient> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<RconResult> ExecuteAsync(string command, CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(opts.Host, ct);
            if (addresses.Length == 0)
            {
                return RconResult.Failure($"DNS resolution returned no addresses for {opts.Host}");
            }

            var address = addresses[0];

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromMilliseconds(opts.RconConnectTimeoutMs));

            using var rcon = new RCON(address, opts.RconPort, opts.RconPassword);
            await rcon.ConnectAsync().WaitAsync(connectCts.Token);

            using var cmdCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cmdCts.CancelAfter(TimeSpan.FromMilliseconds(opts.RconCommandTimeoutMs));

            var output = await rcon.SendCommandAsync(command).WaitAsync(cmdCts.Token);
            return RconResult.Success(output ?? string.Empty);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("RCON command timed out: {Command}", command);
            return RconResult.Failure("timeout");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RCON command failed: {Command}", command);
            return RconResult.Failure(ex.Message);
        }
    }
}
