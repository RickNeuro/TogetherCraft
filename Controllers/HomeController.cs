using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TogetherCraft.Models;
using TogetherCraft.Services;

namespace TogetherCraft.Controllers;

public sealed class HomeController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly AppDatabase _database;

    public HomeController(IConfiguration configuration, AppDatabase database)
    {
        _configuration = configuration;
        _database = database;
    }

    [AllowAnonymous]
    public IActionResult Index() => View();

    [Authorize(Policy = "ApprovedUser")]
    public async Task<IActionResult> Play(CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store, no-cache";

        var idText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idText, out var accountId))
            return Challenge();

        var account = await _database.FindByIdAsync(accountId, cancellationToken);
        if (account is null || (account.Role != "Admin" && account.Status != "Approved"))
            return Forbid();

        var isAdmin = account.Role == "Admin";
        var servers = new List<GameConnectionItem>();

        if (isAdmin || account.WantsFabric)
        {
            servers.Add(new GameConnectionItem(
                "Minecraft · Fabric",
                _configuration["MinecraftServer:DisplayAddress"] ?? ""));
        }

        if (isAdmin || account.WantsCreate)
        {
            servers.Add(new GameConnectionItem(
                "Minecraft · Create",
                _configuration["MinecraftCreateServer:DisplayAddress"] ?? ""));
        }

        return View(new PlayViewModel
        {
            Username = account.Username,
            Servers = servers
        });
    }

    [Authorize(Policy = "AdminOnly")]
    public IActionResult Server() => View();

    [AllowAnonymous]
    public IActionResult Error() => View();
}
