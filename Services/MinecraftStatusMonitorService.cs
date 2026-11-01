using Microsoft.Extensions.Hosting;

namespace TogetherCraft.Services;

public sealed record MinecraftStatusSnapshot(
    MinecraftPingResult Ping,
    DateTimeOffset UpdatedAtUtc);

public sealed class MinecraftStatusMonitorService : BackgroundService
{
    private readonly MinecraftServerStatusService _fabricServer;
    private readonly MinecraftServerStatusService _createServer;
    private readonly ILogger<MinecraftStatusMonitorService> _logger;
    private MinecraftStatusSnapshot _current;
    private MinecraftStatusSnapshot _createCurrent;

    public MinecraftStatusSnapshot Current => _current;
    public MinecraftStatusSnapshot CreateCurrent => _createCurrent;
    public string DisplayAddress => _fabricServer.DisplayAddress;
    public string CreateDisplayAddress => _createServer.DisplayAddress;

    public MinecraftStatusMonitorService(
        MinecraftServerStatusService fabricServer,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        ILogger<MinecraftStatusMonitorService> logger)
    {
        _fabricServer = fabricServer;
        _createServer = new MinecraftServerStatusService(
            configuration,
            loggerFactory.CreateLogger<MinecraftServerStatusService>(),
            "MinecraftCreateServer");
        _logger = logger;

        _current = new MinecraftStatusSnapshot(
            new MinecraftPingResult(false, null, null, null, null, "Проверяем Fabric…"),
            DateTimeOffset.UtcNow);
        _createCurrent = new MinecraftStatusSnapshot(
            new MinecraftPingResult(false, null, null, null, null, "Проверяем Create…"),
            DateTimeOffset.UtcNow);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Ping both worlds independently so one slow tunnel does not delay the other.
                var fabricTask = _fabricServer.ReadAsync(stoppingToken);
                var createTask = _createServer.ReadAsync(stoppingToken);
                await Task.WhenAll(fabricTask, createTask);

                var now = DateTimeOffset.UtcNow;
                _current = new MinecraftStatusSnapshot(await fabricTask, now);
                _createCurrent = new MinecraftStatusSnapshot(await createTask, now);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Не удалось обновить данные игровых серверов Minecraft.");
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