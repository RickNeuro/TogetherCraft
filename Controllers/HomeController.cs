using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using TogetherCraft.Models;
using TogetherCraft.Services;

namespace TogetherCraft.Controllers;

public sealed class HomeController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly AppDatabase _database;
    private readonly IWebHostEnvironment _environment;

    public HomeController(
        IConfiguration configuration,
        AppDatabase database,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _database = database;
        _environment = environment;
    }

    [AllowAnonymous]
    public IActionResult Index() => View();

    [AllowAnonymous]
    public IActionResult Worlds() => View();

    [AllowAnonymous]
    public IActionResult Team() => View();

    [AllowAnonymous]
    public IActionResult Faq() => View();

    [AllowAnonymous]
    public IActionResult Gallery()
    {
        var webRoot = _environment.WebRootPath
            ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var galleryFolder = Path.Combine(webRoot, "images", "gallery");
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

        var images = Directory.Exists(galleryFolder)
            ? Directory.EnumerateFiles(galleryFolder)
                .Where(path => allowedExtensions.Contains(
                    Path.GetExtension(path),
                    StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToList()
            : new List<string>();

        return View(images);
    }

    [Authorize(Policy = "ApprovedUser")]
    public async Task<IActionResult> Play(CancellationToken cancellationToken = default)
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache";
        var account = await GetCurrentAccountAsync(cancellationToken);
        if (account is null)
            return Challenge();

        var isAdmin = account.Role == "Admin";
        if (!isAdmin && account.Status != "Approved")
            return Forbid();

        return View(new PlayViewModel
        {
            Username = account.Username,
            CanAccessVanilla = isAdmin || account.WantsFabric,
            CanAccessCreate = isAdmin || account.WantsCreate
        });
    }

    [Authorize(Policy = "ApprovedUser")]
    [HttpGet]
    public Task<IActionResult> Vanilla(CancellationToken cancellationToken = default)
        => ShowWorldAsync(false, cancellationToken);

    [Authorize(Policy = "ApprovedUser")]
    [HttpGet]
    public Task<IActionResult> Create(CancellationToken cancellationToken = default)
        => ShowWorldAsync(true, cancellationToken);

    private async Task<IActionResult> ShowWorldAsync(bool isCreate, CancellationToken cancellationToken)
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache";
        var account = await GetCurrentAccountAsync(cancellationToken);
        if (account is null)
            return Challenge();

        var isAdmin = account.Role == "Admin";
        if (!isAdmin && account.Status != "Approved")
            return Forbid();

        var hasAccess = isAdmin || (isCreate ? account.WantsCreate : account.WantsFabric);
        if (!hasAccess)
            return Forbid();

        var world = isCreate
            ? new WorldConnectionViewModel
            {
                Username = account.Username,
                Title = "Minecraft · Create",
                Kicker = "МИР 02 · ИНЖЕНЕРИЯ И АВТОМАТИЗАЦИЯ",
                Description = "Отдельный технический мир с Create: шестерни, механизмы, поезда и автоматизация. Здесь строим машины и связываем их в большие системы.",
                Address = _configuration["MinecraftCreateServer:DisplayAddress"] ?? ""
            }
            : new WorldConnectionViewModel
            {
                Username = account.Username,
                Title = "Minecraft · Vanilla",
                Kicker = "МИР 01 · КЛАССИЧЕСКОЕ ВЫЖИВАНИЕ",
                Description = "Классическое Minecraft-выживание: исследование, строительство и совместная игра. Без Create-механик и промышленной автоматизации.",
                Address = _configuration["MinecraftServer:DisplayAddress"] ?? ""
            };

        return View("WorldConnection", world);
    }

    private async Task<UserAccount?> GetCurrentAccountAsync(CancellationToken cancellationToken)
    {
        var idText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idText, out var accountId))
            return null;

        return await _database.FindByIdAsync(accountId, cancellationToken);
    }

    [Authorize(Policy = "AdminOnly")]
    public IActionResult Server() => View();

    [AllowAnonymous]
    public IActionResult Error() => View();
}
