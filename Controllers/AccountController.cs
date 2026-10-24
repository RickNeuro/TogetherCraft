using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TogetherCraft.Models;
using TogetherCraft.Services;

namespace TogetherCraft.Controllers;

public sealed class AccountController : Controller
{
    private readonly AppDatabase _database;
    private readonly IPasswordHasher<UserAccount> _passwordHasher;
    private readonly ILogger<AccountController> _logger;

    public AccountController(AppDatabase database, IPasswordHasher<UserAccount> passwordHasher, ILogger<AccountController> logger)
    {
        _database = database;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AuthActions")]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        ViewBag.ReturnUrl = returnUrl;
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var account = await _database.FindByUsernameAsync(model.Username, cancellationToken);
            if (account is null || _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, model.Password) == PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError(string.Empty, "Неверный логин или пароль.");
                return View(model);
            }

            if (account.Status == "Rejected")
            {
                ModelState.AddModelError(string.Empty, "Заявка на доступ отклонена администратором.");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new(ClaimTypes.Name, account.Username),
                new(ClaimTypes.Role, account.Role),
                new("access_status", account.Status)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            if (account.Status == "Pending")
                return RedirectToAction(nameof(Waiting));
            if (account.Role == "Admin")
                return RedirectToAction("Index", "Admin");
            return RedirectToAction("Server", "Home");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка входа пользователя.");
            ModelState.AddModelError(string.Empty, "Не удалось связаться с базой данных. Проверь, что контейнер PostgreSQL запущен.");
            return View(model);
        }
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AuthActions")]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return View(model);

        var account = new UserAccount
        {
            Username = model.Username.Trim(),
            UsernameNormalized = model.Username.Trim().ToUpperInvariant(),
            MinecraftUsername = model.MinecraftUsername.Trim(),
            Role = "User",
            Status = "Pending",
            CreatedAtUtc = DateTime.UtcNow
        };
        account.PasswordHash = _passwordHasher.HashPassword(account, model.Password);

        try
        {
            if (!await _database.CreateUserAsync(account, cancellationToken))
            {
                ModelState.AddModelError(nameof(model.Username), "Этот логин уже занят.");
                return View(model);
            }
            TempData["Success"] = "Аккаунт создан. Теперь дождись одобрения администратора.";
            return RedirectToAction(nameof(Login));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка регистрации пользователя.");
            ModelState.AddModelError(string.Empty, "Не удалось сохранить заявку. Проверь, что контейнер PostgreSQL запущен.");
            return View(model);
        }
    }

    [Authorize]
    [HttpGet]
    public IActionResult Waiting() => View();

    [HttpGet]
    public IActionResult AccessDenied() => View();

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }
}
