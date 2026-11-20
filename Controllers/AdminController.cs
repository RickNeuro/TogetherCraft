using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TogetherCraft.Services;

namespace TogetherCraft.Controllers;

[Authorize(Policy = "AdminOnly")]
public sealed class AdminController : Controller
{
    private readonly AppDatabase _database;
    private readonly MinecraftRconService _rcon;
    private const long MaxGalleryImageBytes = 8 * 1024 * 1024;
    private static readonly string[] AllowedGalleryExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private readonly ILogger<AdminController> _logger;
    private readonly IWebHostEnvironment _environment;

    public AdminController(
        AppDatabase database,
        MinecraftRconService rcon,
        ILogger<AdminController> logger,
        IWebHostEnvironment environment)
    {
        _database = database;
        _rcon = rcon;
        _logger = logger;
        _environment = environment;
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
                addedServers.Add("Vanilla");
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
    [HttpGet]
    public IActionResult Gallery()
    {
        var folder = GetGalleryFolder();
        Directory.CreateDirectory(folder);

        var images = Directory.EnumerateFiles(folder)
            .Where(path => AllowedGalleryExtensions.Contains(
                Path.GetExtension(path),
                StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderByDescending(name => System.IO.File.GetLastWriteTimeUtc(Path.Combine(folder, name)))
            .ToList();

        return View(images);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxGalleryImageBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxGalleryImageBytes)]
    public async Task<IActionResult> UploadGalleryImage(
        IFormFile? image,
        string? title,
        CancellationToken cancellationToken = default)
    {
        if (image is null || image.Length == 0)
        {
            TempData["Error"] = "Выбери изображение для загрузки.";
            return RedirectToAction(nameof(Gallery));
        }

        if (image.Length > MaxGalleryImageBytes)
        {
            TempData["Error"] = "Изображение слишком большое. Максимальный размер — 8 МБ.";
            return RedirectToAction(nameof(Gallery));
        }

        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        if (!AllowedGalleryExtensions.Contains(extension, StringComparer.Ordinal))
        {
            TempData["Error"] = "Поддерживаются только JPG, PNG, WEBP и GIF.";
            return RedirectToAction(nameof(Gallery));
        }

        await using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, cancellationToken);
        var fileBytes = buffer.ToArray();

        if (!HasValidImageSignature(fileBytes, extension))
        {
            TempData["Error"] = "Содержимое файла не соответствует формату изображения.";
            return RedirectToAction(nameof(Gallery));
        }

        var safeTitle = CreateGallerySlug(title, image.FileName);
        var fileName = $"{safeTitle}--{Guid.NewGuid():N}{extension}";
        var folder = GetGalleryFolder();
        Directory.CreateDirectory(folder);
        var filePath = Path.Combine(folder, fileName);

        try
        {
            await using var output = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await output.WriteAsync(fileBytes, cancellationToken);

            TempData["Success"] = "Изображение добавлено в галерею.";
        }
        catch (IOException exception)
        {
            _logger.LogError(exception, "Не удалось сохранить изображение галереи {FileName}.", fileName);
            TempData["Error"] = "Не удалось сохранить изображение. Проверь свободное место и права на папку галереи.";
        }

        return RedirectToAction(nameof(Gallery));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteGalleryImage(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            || !AllowedGalleryExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Некорректное имя изображения.";
            return RedirectToAction(nameof(Gallery));
        }

        var folder = GetGalleryFolder();
        var filePath = Path.GetFullPath(Path.Combine(folder, fileName));
        var folderPath = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(folderPath, StringComparison.Ordinal))
        {
            TempData["Error"] = "Путь к изображению недопустим.";
            return RedirectToAction(nameof(Gallery));
        }

        if (!System.IO.File.Exists(filePath))
        {
            TempData["Error"] = "Изображение уже удалено или не найдено.";
            return RedirectToAction(nameof(Gallery));
        }

        try
        {
            System.IO.File.Delete(filePath);
            TempData["Success"] = "Изображение удалено из галереи.";
        }
        catch (IOException exception)
        {
            _logger.LogError(exception, "Не удалось удалить изображение галереи {FileName}.", fileName);
            TempData["Error"] = "Не удалось удалить изображение.";
        }

        return RedirectToAction(nameof(Gallery));
    }

    private string GetGalleryFolder()
    {
        var webRoot = _environment.WebRootPath
            ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        return Path.Combine(webRoot, "images", "gallery");
    }

    private static bool HasValidImageSignature(byte[] bytes, string extension)
    {
        if (extension is ".jpg" or ".jpeg")
            return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

        if (extension == ".png")
        {
            byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            return bytes.Length >= signature.Length
                && bytes.AsSpan(0, signature.Length).SequenceEqual(signature);
        }

        if (extension == ".gif")
        {
            return bytes.Length >= 6
                && (System.Text.Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a");
        }

        if (extension == ".webp")
        {
            return bytes.Length >= 12
                && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF"
                && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP";
        }

        return false;
    }

    private static string CreateGallerySlug(string? title, string originalFileName)
    {
        var source = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(originalFileName)
            : title.Trim();

        var cleaned = new string(source
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray());
        var slug = string.Join("-", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (slug.Length > 50)
            slug = slug[..50].Trim('-');

        return string.IsNullOrWhiteSpace(slug) ? "screenshot" : slug;
    }

}
