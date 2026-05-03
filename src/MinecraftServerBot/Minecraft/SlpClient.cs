using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Configuration;

namespace MinecraftServerBot.Minecraft;

/// <summary>
/// Minimal Minecraft Server List Ping (SLP) client. Implements the modern handshake-based
/// status protocol: handshake (0x00) → status request (0x00) → JSON response.
/// </summary>
public sealed class SlpClient : ISlpClient
{
    private const int ProtocolVersion = 770;
    private const int NextStateStatus = 1;

    private readonly IOptionsMonitor<McServerOptions> _options;
    private readonly ILogger<SlpClient> _logger;

    public SlpClient(IOptionsMonitor<McServerOptions> options, ILogger<SlpClient> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<McServerStatus> QueryAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var sw = Stopwatch.StartNew();

        try
        {
            using var tcp = new TcpClient { NoDelay = true };
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromMilliseconds(opts.RconConnectTimeoutMs));
            await tcp.ConnectAsync(opts.Host, opts.QueryPort, connectCts.Token);

            await using var stream = tcp.GetStream();

            await SendHandshakeAsync(stream, opts.Host, opts.QueryPort, ct);
            await SendStatusRequestAsync(stream, ct);

            var json = await ReadStatusResponseAsync(stream, ct);
            sw.Stop();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var online = root.TryGetProperty("players", out var players)
                && players.TryGetProperty("online", out var onlineProp)
                ? onlineProp.GetInt32()
                : 0;

            var max = players.ValueKind == JsonValueKind.Object
                && players.TryGetProperty("max", out var maxProp)
                ? maxProp.GetInt32()
                : 0;

            var motd = ExtractMotd(root);
            var version = root.TryGetProperty("version", out var versionEl)
                && versionEl.TryGetProperty("name", out var versionName)
                ? versionName.GetString() ?? string.Empty
                : string.Empty;

            return new McServerStatus(
                Online: true,
                OnlinePlayers: online,
                MaxPlayers: max,
                Motd: motd,
                Version: version,
                LatencyMs: (int)sw.ElapsedMilliseconds,
                Error: null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("SLP query timed out for {Host}:{Port}", opts.Host, opts.QueryPort);
            return McServerStatus.Offline("timeout");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SLP query failed for {Host}:{Port}", opts.Host, opts.QueryPort);
            return McServerStatus.Offline(ex.GetType().Name);
        }
    }

    private static async Task SendHandshakeAsync(NetworkStream stream, string host, int port, CancellationToken ct)
    {
        using var payload = new MemoryStream();
        WriteVarInt(payload, 0x00);
        WriteVarInt(payload, ProtocolVersion);
        WriteString(payload, host);
        WriteUShort(payload, (ushort)port);
        WriteVarInt(payload, NextStateStatus);

        await WritePacketAsync(stream, payload.ToArray(), ct);
    }

    private static async Task SendStatusRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        using var payload = new MemoryStream();
        WriteVarInt(payload, 0x00);
        await WritePacketAsync(stream, payload.ToArray(), ct);
    }

    private static async Task<string> ReadStatusResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        _ = await ReadVarIntAsync(stream, ct);
        var packetId = await ReadVarIntAsync(stream, ct);
        if (packetId != 0x00)
        {
            throw new InvalidDataException($"Unexpected SLP response packet id 0x{packetId:X2}");
        }

        var jsonLength = await ReadVarIntAsync(stream, ct);
        var buffer = new byte[jsonLength];
        var read = 0;
        while (read < jsonLength)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, jsonLength - read), ct);
            if (n == 0)
            {
                throw new EndOfStreamException("SLP stream closed mid-response");
            }

            read += n;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static async Task WritePacketAsync(NetworkStream stream, byte[] payload, CancellationToken ct)
    {
        using var lengthPrefixed = new MemoryStream();
        WriteVarInt(lengthPrefixed, payload.Length);
        lengthPrefixed.Write(payload, 0, payload.Length);
        var bytes = lengthPrefixed.ToArray();
        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct);
    }

    private static void WriteVarInt(Stream s, int value)
    {
        var v = (uint)value;
        while ((v & 0xFFFFFF80u) != 0)
        {
            s.WriteByte((byte)((v & 0x7F) | 0x80));
            v >>= 7;
        }

        s.WriteByte((byte)v);
    }

    private static void WriteString(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(s, bytes.Length);
        s.Write(bytes, 0, bytes.Length);
    }

    private static void WriteUShort(Stream s, ushort value)
    {
        s.WriteByte((byte)((value >> 8) & 0xFF));
        s.WriteByte((byte)(value & 0xFF));
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken ct)
    {
        var result = 0;
        var shift = 0;
        var buffer = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(0, 1), ct);
            if (n == 0)
            {
                throw new EndOfStreamException("SLP stream closed reading VarInt");
            }

            var b = buffer[0];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift >= 35)
            {
                throw new InvalidDataException("VarInt too long");
            }
        }
    }

    private static string ExtractMotd(JsonElement root)
    {
        if (!root.TryGetProperty("description", out var desc))
        {
            return string.Empty;
        }

        return desc.ValueKind switch
        {
            JsonValueKind.String => desc.GetString() ?? string.Empty,
            JsonValueKind.Object => FlattenChatComponent(desc),
            _ => string.Empty,
        };
    }

    private static string FlattenChatComponent(JsonElement el)
    {
        var sb = new StringBuilder();
        Flatten(el, sb);
        return sb.ToString();
    }

    private static void Flatten(JsonElement el, StringBuilder sb)
    {
        if (el.ValueKind == JsonValueKind.String)
        {
            sb.Append(el.GetString());
            return;
        }

        if (el.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (el.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            sb.Append(text.GetString());
        }

        if (el.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in extra.EnumerateArray())
            {
                Flatten(child, sb);
            }
        }
    }
}
