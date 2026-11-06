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
    private const string ExternalScheme = "External";
    private const string MinecraftScheme = "MicrosoftMinecraft";

    private readonly AppDatabase _database;
    private readonly IPasswordHasher<UserAccount> _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        AppDatabase database,
        IPasswordHasher<UserAccount> passwordHasher,
        IConfiguration configuration,
        ILogger<AccountController> logger)
    {
        _database = database;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
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

            await SignInAccountAsync(account);

            if (account.Role != "Admin" && account.MinecraftUuid is null)
                return RedirectToAction(nameof(ConnectMinecraft));
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

        if (!MicrosoftLoginIsConfigured())
        {
            ModelState.AddModelError(string.Empty, "Подтверждение Minecraft через Microsoft ещё не настроено. Попробуй позже.");
            return View(model);
        }

        var account = new UserAccount
        {
            Username = model.Username.Trim(),
            UsernameNormalized = model.Username.Trim().ToUpperInvariant(),
            Email = model.Email.Trim().ToLowerInvariant(),
            MinecraftUsername = null,
            WantsFabric = model.WantsFabric,
            WantsCreate = model.WantsCreate,
            Role = "User",
            Status = "Pending",
            CreatedAtUtc = DateTime.UtcNow
        };
        account.PasswordHash = _passwordHasher.HashPassword(account, model.Password);

        try
        {
            if (!await _database.CreateUserAsync(account, cancellationToken))
            {
                ModelState.AddModelError(string.Empty, "Этот логин или email уже используется.");
                return View(model);
            }

            await SignInAccountAsync(account);
            return Challenge(CreateMinecraftProperties(), MinecraftScheme);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка регистрации пользователя.");
            ModelState.AddModelError(string.Empty, "Не удалось создать аккаунт. Проверь подключение к базе данных.");
            return View(model);
        }
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> ConnectMinecraft(CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentUserId();
        if (accountId is null)
            return RedirectToAction(nameof(Login));

        var account = await _database.FindByIdAsync(accountId.Value, cancellationToken);
        if (account is null)
            return RedirectToAction(nameof(Login));
        if (account.Role == "Admin")
            return RedirectToAction("Index", "Admin");
        if (account.MinecraftUuid is not null)
            return account.Status == "Pending"
                ? RedirectToAction(nameof(Waiting))
                : RedirectToAction("Server", "Home");

        if (!MicrosoftLoginIsConfigured())
            return View();

        return Challenge(CreateMinecraftProperties(), MinecraftScheme);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> MinecraftCallback(CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentUserId();
        if (accountId is null)
            return RedirectToAction(nameof(Login));

        var external = await HttpContext.AuthenticateAsync(ExternalScheme);
        if (!external.Succeeded || external.Principal is null)
        {
            await HttpContext.SignOutAsync(ExternalScheme);
            TempData["Error"] = "Не удалось подтвердить Minecraft-профиль. Попробуй подключить его ещё раз.";
            return RedirectToAction(nameof(ConnectMinecraft));
        }

        var uuidText = external.Principal.FindFirstValue("minecraft_uuid");
        var minecraftUsername = external.Principal.FindFirstValue("minecraft_username");
        if (!Guid.TryParse(uuidText, out var uuid) || string.IsNullOrWhiteSpace(minecraftUsername))
        {
            await HttpContext.SignOutAsync(ExternalScheme);
            TempData["Error"] = "Microsoft не вернула подтверждённый Minecraft-профиль.";
            return RedirectToAction(nameof(ConnectMinecraft));
        }

        try
        {
            var saved = await _database.SetMinecraftProfileAsync(accountId.Value, uuid, minecraftUsername, cancellationToken);
            await HttpContext.SignOutAsync(ExternalScheme);

            if (!saved)
            {
                TempData["Error"] = "Этот профиль Minecraft уже привязан к другому аккаунту или заявка была обработана.";
                return RedirectToAction(nameof(ConnectMinecraft));
            }

            TempData["Success"] = $"Профиль Minecraft подтверждён: {minecraftUsername}.";
            var updatedAccount = await _database.FindByIdAsync(accountId.Value, cancellationToken);
            if (updatedAccount?.Status == "Approved")
                return RedirectToAction("Server", "Home");
            return RedirectToAction(nameof(Waiting));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось сохранить Minecraft-профиль для аккаунта {AccountId}.", accountId);
            await HttpContext.SignOutAsync(ExternalScheme);
            TempData["Error"] = "Не удалось сохранить Minecraft-профиль. Попробуй ещё раз.";
            return RedirectToAction(nameof(ConnectMinecraft));
        }
    }

    [Authorize]
    [HttpGet]
    public IActionResult MinecraftFailed()
    {
        return View();
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
        await HttpContext.SignOutAsync(ExternalScheme);
        return RedirectToAction("Index", "Home");
    }

    private bool MicrosoftLoginIsConfigured()
    {
        return !string.IsNullOrWhiteSpace(_configuration["MicrosoftMinecraft:ClientId"])
            && !string.IsNullOrWhiteSpace(_configuration["MicrosoftMinecraft:ClientSecret"]);
    }

    private AuthenticationProperties CreateMinecraftProperties()
    {
        return new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(MinecraftCallback), "Account") ?? "/Account/MinecraftCallback"
        };
    }

    private long? GetCurrentUserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(id, out var accountId) ? accountId : null;
    }

    private Task SignInAccountAsync(UserAccount account)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.Username),
            new(ClaimTypes.Role, account.Role),
            new("access_status", account.Status)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }
}
