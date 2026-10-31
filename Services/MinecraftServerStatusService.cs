using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TogetherCraft.Services;

public sealed record MinecraftPingResult(
    bool IsOnline,
    string? Version,
    int? OnlinePlayers,
    int? MaxPlayers,
    double? PingMs,
    string Message);

public sealed class MinecraftServerStatusService
{
    private readonly string _host;
    private readonly int _port;
    public string DisplayAddress { get; }

    public MinecraftServerStatusService(IConfiguration configuration)
    {
        _host = configuration["MinecraftServer:Host"] ?? "fried-newer.tun.ply.gg";
        _port = int.TryParse(configuration["MinecraftServer:Port"], out var port) && port is > 0 and <= 65535
            ? port
            : 32219;
        DisplayAddress = configuration["MinecraftServer:DisplayAddress"] ?? _host;
    }

    public async Task<MinecraftPingResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));

        var started = Stopwatch.GetTimestamp();

        try
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(_host, _port, timeout.Token);
            await using var stream = client.GetStream();

            using var handshake = new MemoryStream();
            WriteVarInt(handshake, 0);
            WriteVarInt(handshake, -1);

            var hostBytes = Encoding.UTF8.GetBytes(_host);
            WriteVarInt(handshake, hostBytes.Length);
            handshake.Write(hostBytes);

            var portBytes = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(portBytes, (ushort)_port);
            handshake.Write(portBytes);
            WriteVarInt(handshake, 1);

            await WritePacketAsync(stream, handshake.ToArray(), timeout.Token);
            await WritePacketAsync(stream, new byte[] { 0 }, timeout.Token);

            var packetLength = await ReadVarIntAsync(stream, timeout.Token);
            if (packetLength is <= 0 or > 1_048_576)
                throw new InvalidOperationException("Некорректный размер ответа Minecraft.");

            var packet = new byte[packetLength];
            await stream.ReadExactlyAsync(packet.AsMemory(), timeout.Token);

            using var packetStream = new MemoryStream(packet);
            var packetId = ReadVarInt(packetStream);
            if (packetId != 0)
                throw new InvalidOperationException("Minecraft вернул неожиданный пакет.");

            var jsonLength = ReadVarInt(packetStream);
            if (jsonLength is <= 0 or > 1_000_000 || jsonLength > packetStream.Length - packetStream.Position)
                throw new InvalidOperationException("Некорректный ответ статуса Minecraft.");

            var jsonBytes = new byte[jsonLength];
            await packetStream.ReadExactlyAsync(jsonBytes.AsMemory(), timeout.Token);

            using var json = JsonDocument.Parse(jsonBytes);
            var root = json.RootElement;

            string? version = null;
            int? online = null;
            int? maximum = null;

            if (root.TryGetProperty("version", out var versionElement)
                && versionElement.TryGetProperty("name", out var versionName)
                && versionName.ValueKind == JsonValueKind.String)
            {
                version = versionName.GetString();
            }

            if (root.TryGetProperty("players", out var players))
            {
                online = ReadInt(players, "online");
                maximum = ReadInt(players, "max");
            }

            var ping = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return new MinecraftPingResult(true, version, online, maximum, ping, "Minecraft отвечает.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is SocketException
            or IOException
            or JsonException
            or InvalidOperationException
            or OperationCanceledException)
        {
            return new MinecraftPingResult(false, null, null, null, null,
                "Minecraft сейчас не отвечает через Playit.");
        }
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static async Task WritePacketAsync(NetworkStream stream, byte[] payload, CancellationToken cancellationToken)
    {
        using var packet = new MemoryStream();
        WriteVarInt(packet, payload.Length);
        packet.Write(payload);
        await stream.WriteAsync(packet.ToArray(), cancellationToken);
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        var remaining = unchecked((uint)value);
        while ((remaining & 0xFFFFFF80) != 0)
        {
            stream.WriteByte((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }

        stream.WriteByte((byte)remaining);
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var result = 0;
        var oneByte = new byte[1];

        for (var shift = 0; shift < 35; shift += 7)
        {
            await stream.ReadExactlyAsync(oneByte.AsMemory(), cancellationToken);
            var current = oneByte[0];
            result |= (current & 0x7F) << shift;

            if ((current & 0x80) == 0)
                return result;
        }

        throw new InvalidOperationException("Некорректный VarInt от Minecraft.");
    }

    private static int ReadVarInt(Stream stream)
    {
        var result = 0;

        for (var shift = 0; shift < 35; shift += 7)
        {
            var current = stream.ReadByte();
            if (current < 0)
                throw new EndOfStreamException();

            result |= (current & 0x7F) << shift;
            if ((current & 0x80) == 0)
                return result;
        }

        throw new InvalidOperationException("Некорректный VarInt в ответе Minecraft.");
    }
}
