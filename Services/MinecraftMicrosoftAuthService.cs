using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TogetherCraft.Services;

public sealed class MinecraftMicrosoftAuthService
{
    private readonly HttpClient _http;

    public MinecraftMicrosoftAuthService(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<MinecraftJavaProfile> GetProfileAsync(string microsoftAccessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(microsoftAccessToken))
            throw new InvalidOperationException("Microsoft не вернула токен авторизации.");

        using var xboxResponse = await PostJsonAsync(
            "https://user.auth.xboxlive.com/user/authenticate",
            new
            {
                Properties = new
                {
                    AuthMethod = "RPS",
                    SiteName = "user.auth.xboxlive.com",
                    RpsTicket = "d=" + microsoftAccessToken
                },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT"
            },
            cancellationToken);

        var xboxToken = xboxResponse.RootElement.GetProperty("Token").GetString();
        var userHash = xboxResponse.RootElement
            .GetProperty("DisplayClaims")
            .GetProperty("xui")[0]
            .GetProperty("uhs")
            .GetString();

        if (string.IsNullOrWhiteSpace(xboxToken) || string.IsNullOrWhiteSpace(userHash))
            throw new InvalidOperationException("Xbox Live не вернул данные пользователя.");

        using var xstsResponse = await PostJsonAsync(
            "https://xsts.auth.xboxlive.com/xsts/authorize",
            new
            {
                Properties = new
                {
                    SandboxId = "RETAIL",
                    UserTokens = new[] { xboxToken }
                },
                RelyingParty = "rp://api.minecraftservices.com/",
                TokenType = "JWT"
            },
            cancellationToken);

        var xstsToken = xstsResponse.RootElement.GetProperty("Token").GetString();
        if (string.IsNullOrWhiteSpace(xstsToken))
            throw new InvalidOperationException("Xbox Security Token Service не вернул токен.");

        using var minecraftResponse = await PostJsonAsync(
            "https://api.minecraftservices.com/authentication/login_with_xbox",
            new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" },
            cancellationToken);

        var minecraftToken = minecraftResponse.RootElement.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(minecraftToken))
            throw new InvalidOperationException("Minecraft Services не выдал токен профиля.");

        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftToken);

        using var profileResponse = await _http.SendAsync(profileRequest, cancellationToken);
        if (!profileResponse.IsSuccessStatusCode)
        {
            if (profileResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException("У этого Microsoft-аккаунта не найден профиль Minecraft Java Edition.");
            throw new InvalidOperationException("Minecraft Services не смог подтвердить профиль. Проверь аккаунт и повтори попытку.");
        }

        using var profileJson = await JsonDocument.ParseAsync(await profileResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = profileJson.RootElement;
        var id = root.GetProperty("id").GetString();
        var name = root.GetProperty("name").GetString();

        if (!Guid.TryParseExact(id, "N", out var uuid) && !Guid.TryParse(id, out uuid))
            throw new InvalidOperationException("Minecraft Services вернул некорректный UUID профиля.");

        if (string.IsNullOrWhiteSpace(name) || name.Length > 16)
            throw new InvalidOperationException("Minecraft Services вернул некорректный ник.");

        return new MinecraftJavaProfile(uuid, name);
    }

    private async Task<JsonDocument> PostJsonAsync(string url, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Не удалось пройти один из этапов проверки Microsoft/Xbox. Проверь настройки приложения Microsoft и доступ к Xbox Live.");

        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }
}

public sealed record MinecraftJavaProfile(Guid Uuid, string Username);
