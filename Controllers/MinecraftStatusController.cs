using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TogetherCraft.Services;

namespace TogetherCraft.Controllers;

[ApiController]
[Route("api/minecraft/status")]
[Authorize(Policy = "ApprovedUser")]
public sealed class MinecraftStatusController : ControllerBase
{
    private readonly MinecraftServerStatusService _server;
    private readonly MinecraftRconService _rcon;

    public MinecraftStatusController(MinecraftServerStatusService server, MinecraftRconService rcon)
    {
        _server = server;
        _rcon = rcon;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var pingTask = _server.ReadAsync(cancellationToken);
        var sparkTask = _rcon.ReadSparkMetricsAsync(cancellationToken);

        await Task.WhenAll(pingTask, sparkTask);

        var ping = await pingTask;
        var spark = await sparkTask;

        return Ok(new
        {
            address = _server.DisplayAddress,
            isOnline = ping.IsOnline,
            version = ping.Version,
            onlinePlayers = ping.OnlinePlayers,
            maxPlayers = ping.MaxPlayers,
            pingMs = ping.PingMs,
            tps = spark.Tps,
            msptP95 = spark.MsptP95,
            sparkAvailable = spark.Available,
            message = ping.Message,
            sparkMessage = spark.Message,
            updatedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
