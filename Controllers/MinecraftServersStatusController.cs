using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TogetherCraft.Services;

namespace TogetherCraft.Controllers;

[ApiController]
[Route("api/minecraft/servers")]
[Authorize(Policy = "ApprovedUser")]
public sealed class MinecraftServersStatusController : ControllerBase
{
    private readonly MinecraftStatusMonitorService _monitor;

    public MinecraftServersStatusController(MinecraftStatusMonitorService monitor)
    {
        _monitor = monitor;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            fabric = ToResponse(_monitor.Current, _monitor.DisplayAddress),
            create = ToResponse(_monitor.CreateCurrent, _monitor.CreateDisplayAddress)
        });
    }

    private static object ToResponse(MinecraftStatusSnapshot snapshot, string address)
    {
        var ping = snapshot.Ping;
        return new
        {
            address,
            isOnline = ping.IsOnline,
            version = ping.Version,
            onlinePlayers = ping.OnlinePlayers,
            maxPlayers = ping.MaxPlayers,
            pingMs = ping.PingMs,
            message = ping.Message,
            updatedAtUtc = snapshot.UpdatedAtUtc
        };
    }
}