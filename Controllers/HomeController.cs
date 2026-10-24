using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TogetherCraft.Controllers;

public sealed class HomeController : Controller
{
    [AllowAnonymous]
    public IActionResult Index() => View();

    [Authorize(Policy = "ApprovedUser")]
    public IActionResult Server() => View();

    [AllowAnonymous]
    public IActionResult Error() => View();
}
