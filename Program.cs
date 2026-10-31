using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Security.Claims;
using TogetherCraft.Models;
using TogetherCraft.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthActions", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionPath))
{
    Directory.CreateDirectory(dataProtectionPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
        .SetApplicationName("TogetherCraft");
}
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Web-Container публикует порт 8080 только локально; внешний доступ идёт через обратный прокси.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddSingleton<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddSingleton<AppDatabase>();
builder.Services.AddSingleton<MinecraftRconService>();
builder.Services.AddSingleton<MinecraftServerStatusService>();
builder.Services.AddHttpClient<PrometheusMetricsService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Prometheus:BaseUrl"] ?? "http://prometheus:9090");
    client.Timeout = TimeSpan.FromSeconds(4);
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TogetherCraft.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var idText = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!long.TryParse(idText, out var userId))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                UserAccount? account;
                try
                {
                    var database = context.HttpContext.RequestServices.GetRequiredService<AppDatabase>();
                    account = await database.FindByIdAsync(userId, context.HttpContext.RequestAborted);
                }
                catch (Exception exception)
                {
                    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("TogetherCraft.CookieValidation")
                        .LogWarning(exception, "Не удалось проверить пользователя в PostgreSQL.");
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                if (account is null || account.Status == "Rejected")
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                if (context.Principal?.Identity is not ClaimsIdentity identity)
                    return;

                var oldRole = identity.FindFirst(ClaimTypes.Role)?.Value;
                var oldStatus = identity.FindFirst("access_status")?.Value;
                if (oldRole == account.Role && oldStatus == account.Status)
                    return;

                foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList())
                    identity.RemoveClaim(claim);
                foreach (var claim in identity.FindAll("access_status").ToList())
                    identity.RemoveClaim(claim);
                identity.AddClaim(new Claim(ClaimTypes.Role, account.Role));
                identity.AddClaim(new Claim("access_status", account.Status));
                context.ReplacePrincipal(new ClaimsPrincipal(identity));
                context.ShouldRenew = true;
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApprovedUser", policy => policy.RequireAuthenticatedUser()
        .RequireAssertion(context => context.User.IsInRole("Admin") || context.User.HasClaim("access_status", "Approved")));
    options.AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
});

var app = builder.Build();

try
{
    var database = app.Services.GetRequiredService<AppDatabase>();
    await database.InitializeAsync(app.Configuration["Admin:Username"], app.Configuration["Admin:Password"]);
}
catch (Exception exception)
{
    app.Logger.LogError(exception, "Не удалось инициализировать PostgreSQL.");
    if (app.Environment.IsProduction())
        throw;
    app.Logger.LogWarning("В режиме разработки сайт продолжит запуск, но регистрация и вход требуют работающий PostgreSQL.");
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", async (AppDatabase database, CancellationToken cancellationToken) =>
{
    IResult result;
    try
    {
        await database.IsAvailableAsync(cancellationToken);
        result = Results.Content("{\"status\":\"ok\",\"database\":\"connected\"}", "application/json", statusCode: StatusCodes.Status200OK);
    }
    catch
    {
        result = Results.Content("{\"status\":\"degraded\",\"database\":\"unavailable\"}", "application/json", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    return result;
});

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
