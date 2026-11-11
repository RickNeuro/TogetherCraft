using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TogetherCraft.Services;

namespace TogetherCraft.Controllers;

[ApiController]
[Route("api/minecraft/status")]
[Authorize(Policy = "AdminOnly")]
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

        return Ok(new
        {
            address = _monitor.DisplayAddress,
            isOnline = ping.IsOnline,
            version = ping.Version,
            onlinePlayers = ping.OnlinePlayers,
            maxPlayers = ping.MaxPlayers,
            pingMs = ping.PingMs,
            message = ping.Message,
            updatedAtUtc = snapshot.UpdatedAtUtc
        });
    }
}
