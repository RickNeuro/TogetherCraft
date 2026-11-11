using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TogetherCraft.Services;

namespace TogetherCraft.Controllers;

[Authorize(Policy = "AdminOnly")]
public sealed class AdminController : Controller
{
    private readonly AppDatabase _database;
    private readonly MinecraftRconService _rcon;
    private readonly ILogger<AdminController> _logger;

    public AdminController(AppDatabase database, MinecraftRconService rcon, ILogger<AdminController> logger)
    {
        _database = database;
        _rcon = rcon;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        try
        {
            return View(await _database.GetPendingUsersAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось загрузить заявки.");
            TempData["Error"] = "Не удалось прочитать заявки из PostgreSQL.";
            return View(Array.Empty<TogetherCraft.Models.UserAccount>());
        }
    }

    [HttpGet]
    public async Task<IActionResult> Users(CancellationToken cancellationToken = default)
    {
        try
        {
            return View(await _database.GetUsersAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось загрузить список пользователей.");
            TempData["Error"] = "Не удалось загрузить пользователей из PostgreSQL.";
            return View(Array.Empty<TogetherCraft.Models.AdminUserViewModel>());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(long id, string role, CancellationToken cancellationToken = default)
    {
        if (role is not ("Admin" or "User"))
        {
            TempData["Error"] = "Неизвестная роль пользователя.";
            return RedirectToAction(nameof(Users));
        }

        var currentIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (long.TryParse(currentIdText, out var currentId) && currentId == id && role == "User")
        {
            TempData["Error"] = "Нельзя снять административную роль со своей текущей учётной записи здесь.";
            return RedirectToAction(nameof(Users));
        }

        try
        {
            var changed = await _database.SetUserRoleAsync(id, role, cancellationToken);
            TempData[changed ? "Success" : "Error"] = changed
                ? (role == "Admin" ? "Пользователь назначен администратором." : "Административная роль снята.")
                : "Не удалось изменить роль. Нельзя снять роль у последнего администратора, а отклонённые аккаунты нельзя назначить администраторами.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось изменить роль пользователя {AccountId}.", id);
            TempData["Error"] = "Не удалось изменить роль пользователя.";
        }

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(long id, CancellationToken cancellationToken = default)
    {
        var account = await _database.FindByIdAsync(id, cancellationToken);
        if (account is null || account.Status != "Pending" || account.Role != "User")
        {
            TempData["Error"] = "Заявка не найдена или уже обработана.";
            return RedirectToAction(nameof(Index));
        }

        if (account.MinecraftUuid is null || string.IsNullOrWhiteSpace(account.MinecraftUsername))
        {
            TempData["Error"] = "Сначала пользователь должен подтвердить Minecraft Java через Microsoft.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var addedServers = new List<string>();

            if (account.WantsFabric)
            {
                await _rcon.AddToWhitelistAsync(account.MinecraftUsername, cancellationToken);
                addedServers.Add("Fabric");
            }

            if (account.WantsCreate)
            {
                await _rcon.AddToCreateWhitelistAsync(account.MinecraftUsername, cancellationToken);
                addedServers.Add("Create");
            }

            if (!await _database.SetStatusAsync(account.Id, "Approved", cancellationToken))
            {
                TempData["Error"] = "Игровые серверы подтвердили добавление, но статус заявки изменился. Проверь её перед повторным действием.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = $"{account.Username} одобрен. Whitelist обновлён: {string.Join(", ", addedServers)}.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось одобрить заявку {AccountId}.", account.Id);
            TempData["Error"] = exception.Message + " Заявка осталась ожидающей. Повторное добавление в whitelist безопасно.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(long id, CancellationToken cancellationToken = default)
    {
        try
        {
            var rejected = await _database.SetStatusAsync(id, "Rejected", cancellationToken);
            TempData[rejected ? "Success" : "Error"] = rejected
                ? "Заявка отклонена."
                : "Заявка не найдена или уже была обработана.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось отклонить заявку {AccountId}.", id);
            TempData["Error"] = "Не удалось сохранить решение в PostgreSQL.";
        }
        return RedirectToAction(nameof(Index));
    }
}
