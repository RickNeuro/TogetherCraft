using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace TogetherCraft.Services;

public sealed class MinecraftRconService
{
    private readonly RconConnection _fabric;
    private readonly RconConnection _create;

    public MinecraftRconService(IConfiguration configuration)
    {
        _fabric = ReadConnection(configuration, "MinecraftRcon", 25575);
        _create = ReadConnection(configuration, "MinecraftCreateRcon", 25576);
    }

    public Task AddToWhitelistAsync(string minecraftUsername, CancellationToken cancellationToken = default)
    {
        return AddToWhitelistAsync(_fabric, "Fabric", minecraftUsername, cancellationToken);
    }

    public Task AddToCreateWhitelistAsync(string minecraftUsername, CancellationToken cancellationToken = default)
    {
        return AddToWhitelistAsync(_create, "Create", minecraftUsername, cancellationToken);
    }

    private static RconConnection ReadConnection(IConfiguration configuration, string section, int defaultPort)
    {
        var settings = configuration.GetSection(section);
        var host = settings["Host"] ?? "host.docker.internal";
        var port = int.TryParse(settings["Port"], out var configuredPort) ? configuredPort : defaultPort;
        var password = settings["Password"] ?? "";
        return new RconConnection(host, port, password);
    }

    private static async Task AddToWhitelistAsync(
        RconConnection connection,
        string serverName,
        string minecraftUsername,
        CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(minecraftUsername, "^[A-Za-z0-9_]{3,16}$"))
            throw new InvalidOperationException("Minecraft-ник имеет неверный формат.");
        if (string.IsNullOrWhiteSpace(connection.Password)
            || connection.Password.Contains("replace", StringComparison.OrdinalIgnoreCase)
            || connection.Password.Contains("change_me", StringComparison.OrdinalIgnoreCase)
            || connection.Password.Contains("changeme", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"RCON сервера {serverName} ещё не настроен. Задай пароль в .env и включи RCON в server.properties.");
        if (connection.Port is < 1 or > 65535)
            throw new InvalidOperationException($"Неверный порт RCON сервера {serverName}.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(connection.Host, connection.Port, timeout.Token);
            await using var stream = client.GetStream();
            await AuthenticateAsync(stream, connection.Password, timeout.Token);

            await SendPacketAsync(stream, 2, 2, $"whitelist add {minecraftUsername}", timeout.Token);
            var response = await ReadPacketAsync(stream, timeout.Token);
            if (response.Id != 2)
                throw new InvalidOperationException($"Сервер {serverName} не подтвердил команду whitelist.");

            var message = response.Body.Trim();
            if (!message.Contains("added", StringComparison.OrdinalIgnoreCase)
                && !message.Contains("already", StringComparison.OrdinalIgnoreCase)
                && !message.Contains("добавлен", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Сервер {serverName} не подтвердил добавление ника. Ответ: {message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"RCON сервера {serverName} не ответил за 6 секунд. Проверь службу, порт и доступ из Docker.");
        }
        catch (SocketException)
        {
            throw new InvalidOperationException($"Не удалось подключиться к RCON сервера {serverName}. Проверь host, порт, firewall и server.properties.");
        }
    }

    private static async Task AuthenticateAsync(NetworkStream stream, string password, CancellationToken cancellationToken)
    {
        await SendPacketAsync(stream, 1, 3, password, cancellationToken);
        var auth = await ReadPacketAsync(stream, cancellationToken);
        if (auth.Id == -1 || auth.Id != 1)
            throw new InvalidOperationException("Minecraft отклонил авторизацию RCON. Проверь пароль в .env и server.properties.");
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
        var bodyLength = Math.Max(0, length - 10);
        var body = Encoding.UTF8.GetString(content, 8, bodyLength);
        return new RconPacket(id, body);
    }

    private sealed record RconConnection(string Host, int Port, string Password);
    private sealed record RconPacket(int Id, string Body);
}