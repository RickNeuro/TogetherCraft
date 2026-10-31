using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TogetherCraft.Services;

namespace TogetherCraft.Controllers;

[ApiController]
[Route("api/minecraft/status")]
[Authorize(Policy = "ApprovedUser")]
public sealed class MinecraftStatusController : ControllerBase
{
    private readonly MinecraftStatusMonitorService _monitor;

    public MinecraftStatusController(MinecraftStatusMonitorService monitor)
    {
        _monitor = monitor;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var snapshot = _monitor.Current;
        var ping = snapshot.Ping;
        var spark = snapshot.Spark;

        return Ok(new
        {
            address = _monitor.DisplayAddress,
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
            updatedAtUtc = snapshot.UpdatedAtUtc
        });
    }
}
