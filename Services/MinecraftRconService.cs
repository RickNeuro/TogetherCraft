using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace TogetherCraft.Services;

public sealed record SparkMetricsResult(bool Available, double? Tps, double? MsptP95, string Message);

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
            await AuthenticateAsync(stream, timeout.Token);

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

    public async Task<SparkMetricsResult> ReadSparkMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_password)
            || _password.Contains("change_me", StringComparison.OrdinalIgnoreCase)
            || _password.Contains("changeme", StringComparison.OrdinalIgnoreCase)
            || _port is < 1 or > 65535)
        {
            return new SparkMetricsResult(false, null, null, "Для TPS/MSPT нужно настроить RCON на сервере.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port, timeout.Token);
            await using var stream = client.GetStream();

            await AuthenticateAsync(stream, timeout.Token);
            await SendPacketAsync(stream, 2, 2, "spark tps", timeout.Token);

            var response = await ReadPacketAsync(stream, timeout.Token);
            if (response.Id != 2 || string.IsNullOrWhiteSpace(response.Body))
                return new SparkMetricsResult(false, null, null, "RCON доступен, но Spark не вернул данные.");

            var tpsMatch = Regex.Match(
                response.Body,
                @"TPS from last 5s,\s*10s,\s*1m,\s*5m,\s*15m:\s*([0-9]+(?:\.[0-9]+)?),\s*([0-9]+(?:\.[0-9]+)?),\s*([0-9]+(?:\.[0-9]+)?),\s*([0-9]+(?:\.[0-9]+)?),\s*([0-9]+(?:\.[0-9]+)?)",
                RegexOptions.IgnoreCase);

            var tickMatch = Regex.Match(
                response.Body,
                @"Tick durations.*?:\s*([0-9]+(?:\.[0-9]+)?)/([0-9]+(?:\.[0-9]+)?)/([0-9]+(?:\.[0-9]+)?)/([0-9]+(?:\.[0-9]+)?)\s*;\s*([0-9]+(?:\.[0-9]+)?)/([0-9]+(?:\.[0-9]+)?)/([0-9]+(?:\.[0-9]+)?)/([0-9]+(?:\.[0-9]+)?)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!tpsMatch.Success || !tickMatch.Success)
                return new SparkMetricsResult(false, null, null, "Spark ответил, но формат данных не распознан.");

            var tps = ParseNumber(tpsMatch.Groups[3].Value);
            var msptP95 = ParseNumber(tickMatch.Groups[7].Value);

            return new SparkMetricsResult(
                true,
                tps,
                msptP95,
                "TPS и MSPT получены через Spark.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SparkMetricsResult(false, null, null, "RCON/Spark не ответил за 3 секунды.");
        }
        catch (Exception exception) when (exception is SocketException or IOException or InvalidOperationException)
        {
            return new SparkMetricsResult(false, null, null, "Не удалось получить TPS/MSPT через RCON. Проверь RCON и Spark.");
        }
    }

    private async Task AuthenticateAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        await SendPacketAsync(stream, 1, 3, _password, cancellationToken);
        var auth = await ReadPacketAsync(stream, cancellationToken);
        if (auth.Id == -1 || auth.Id != 1)
            throw new InvalidOperationException("Minecraft отклонил авторизацию RCON. Проверь пароль в .env и server.properties.");
    }

    private static double? ParseNumber(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
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
