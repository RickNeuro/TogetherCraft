using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace TogetherCraft.Services;

public sealed class MinecraftRconService
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _password;

    public MinecraftRconService(IConfiguration configuration)
    {
        _host = configuration["MinecraftRcon:Host"] ?? "host.docker.internal";
        _port = int.TryParse(configuration["MinecraftRcon:Port"], out var port) ? port : 25575;
        _password = configuration["MinecraftRcon:Password"] ?? "";
    }

    public async Task AddToWhitelistAsync(string minecraftUsername, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(minecraftUsername, "^[A-Za-z0-9_]{3,16}$"))
            throw new InvalidOperationException("Minecraft-ник имеет неверный формат.");
        if (string.IsNullOrWhiteSpace(_password) || (_password.Contains("replace", StringComparison.OrdinalIgnoreCase) || _password.Contains("change_me", StringComparison.OrdinalIgnoreCase) || _password.Contains("changeme", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("RCON ещё не настроен. Задай RCON_PASSWORD в .env и включи RCON в server.properties.");
        if (_port is < 1 or > 65535)
            throw new InvalidOperationException("Неверный порт RCON.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(_host, _port, timeout.Token);
            await using var stream = client.GetStream();
            await SendPacketAsync(stream, 1, 3, _password, timeout.Token);
            var auth = await ReadPacketAsync(stream, timeout.Token);
            if (auth.Id == -1 || auth.Id != 1)
                throw new InvalidOperationException("Minecraft отклонил авторизацию RCON. Проверь пароль в .env и server.properties.");

            await SendPacketAsync(stream, 2, 2, $"whitelist add {minecraftUsername}", timeout.Token);
            var response = await ReadPacketAsync(stream, timeout.Token);
            if (response.Id != 2)
                throw new InvalidOperationException("Minecraft не подтвердил команду добавления в whitelist.");

            var message = response.Body.Trim();
            if (!message.Contains("added", StringComparison.OrdinalIgnoreCase)
                && !message.Contains("already", StringComparison.OrdinalIgnoreCase)
                && !message.Contains("добавлен", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Minecraft не подтвердил добавление ника. Ответ сервера: {message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Minecraft RCON не ответил за 6 секунд. Проверь службу сервера, порт и доступ из Docker.");
        }
        catch (SocketException)
        {
            throw new InvalidOperationException("Не удалось подключиться к Minecraft RCON. Проверь RCON_HOST, порт, firewall и server.properties.");
        }
    }

    private static async Task SendPacketAsync(NetworkStream stream, int id, int type, string body, CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var packet = new byte[bodyBytes.Length + 14];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), bodyBytes.Length + 10);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), type);
        bodyBytes.AsSpan().CopyTo(packet.AsSpan(12));
        packet[^2] = 0;
        packet[^1] = 0;
        await stream.WriteAsync(packet.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<RconPacket> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header.AsMemory(), cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 10 or > 1_048_576)
            throw new InvalidOperationException("Minecraft вернул некорректный RCON-ответ.");

        var content = new byte[length];
        await stream.ReadExactlyAsync(content.AsMemory(), cancellationToken);
        var id = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(0, 4));
        var type = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(4, 4));
        var bodyLength = Math.Max(0, length - 10);
        var body = Encoding.UTF8.GetString(content, 8, bodyLength);
        return new RconPacket(id, type, body);
    }

    private sealed record RconPacket(int Id, int Type, string Body);
}
