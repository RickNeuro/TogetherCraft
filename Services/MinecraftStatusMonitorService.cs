using Microsoft.Extensions.Hosting;

namespace TogetherCraft.Services;

public sealed record MinecraftStatusSnapshot(
    MinecraftPingResult Ping,
    DateTimeOffset UpdatedAtUtc);

public sealed class MinecraftStatusMonitorService : BackgroundService
{
    private readonly MinecraftServerStatusService _server;
    private readonly ILogger<MinecraftStatusMonitorService> _logger;
    private MinecraftStatusSnapshot _current;

    public MinecraftStatusSnapshot Current => _current;
    public string DisplayAddress => _server.DisplayAddress;

    public MinecraftStatusMonitorService(
        MinecraftServerStatusService server,
        ILogger<MinecraftStatusMonitorService> logger)
    {
        _server = server;
        _logger = logger;

        _current = new MinecraftStatusSnapshot(
            new MinecraftPingResult(false, null, null, null, null, "Проверяем Minecraft…"),
            DateTimeOffset.UtcNow);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ping = await _server.ReadAsync(stoppingToken);
                _current = new MinecraftStatusSnapshot(ping, DateTimeOffset.UtcNow);
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