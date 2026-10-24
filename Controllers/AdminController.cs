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

        try
        {
            // Сначала добавляем ник в Minecraft; только при подтверждении меняем статус заявки.
            await _rcon.AddToWhitelistAsync(account.MinecraftUsername, cancellationToken);
            if (!await _database.SetStatusAsync(account.Id, "Approved", cancellationToken))
            {
                TempData["Error"] = "Minecraft подтвердил добавление, но статус заявки изменился. Обнови список и проверь заявку.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = $"{account.Username} одобрен, ник {account.MinecraftUsername} добавлен в whitelist Minecraft.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось одобрить заявку {AccountId}.", account.Id);
            TempData["Error"] = exception.Message + " Заявка осталась ожидающей — после настройки можно попробовать снова.";
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
