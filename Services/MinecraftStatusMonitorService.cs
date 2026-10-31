using Microsoft.Extensions.Hosting;

namespace TogetherCraft.Services;

public sealed record MinecraftStatusSnapshot(
    MinecraftPingResult Ping,
    SparkMetricsResult Spark,
    DateTimeOffset UpdatedAtUtc);

public sealed class MinecraftStatusMonitorService : BackgroundService
{
    private readonly MinecraftServerStatusService _server;
    private readonly MinecraftRconService _rcon;
    private readonly ILogger<MinecraftStatusMonitorService> _logger;
    private MinecraftStatusSnapshot _current;

    public MinecraftStatusSnapshot Current => _current;
    public string DisplayAddress => _server.DisplayAddress;

    public MinecraftStatusMonitorService(
        MinecraftServerStatusService server,
        MinecraftRconService rcon,
        ILogger<MinecraftStatusMonitorService> logger)
    {
        _server = server;
        _rcon = rcon;
        _logger = logger;

        _current = new MinecraftStatusSnapshot(
            new MinecraftPingResult(false, null, null, null, null, "Проверяем Minecraft…"),
            new SparkMetricsResult(false, null, null, "Ожидаем данные Spark."),
            DateTimeOffset.UtcNow);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pingTask = _server.ReadAsync(stoppingToken);
                var sparkTask = _rcon.ReadSparkMetricsAsync(stoppingToken);

                await Task.WhenAll(pingTask, sparkTask);

                var ping = await pingTask;
                var spark = await sparkTask;

                _current = new MinecraftStatusSnapshot(ping, spark, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Не удалось обновить данные Minecraft.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
