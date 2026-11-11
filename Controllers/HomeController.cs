using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TogetherCraft.Controllers;

public sealed class HomeController : Controller
{
    private readonly IConfiguration _configuration;

    public HomeController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [AllowAnonymous]
    public IActionResult Index() => View();

    [Authorize(Policy = "ApprovedUser")]
    public IActionResult Play()
    {
        ViewBag.FabricAddress = _configuration["MinecraftServer:DisplayAddress"] ?? "";
        return View();
    }

    [Authorize(Policy = "AdminOnly")]
    public IActionResult Server() => View();

    [AllowAnonymous]
    public IActionResult Error() => View();
}
