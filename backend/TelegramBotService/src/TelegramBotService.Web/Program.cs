using System.Globalization;
using Framework.Endpoints;
using Observability;
using PlatformBootstrap;
using Serilog;
using TelegramBotFlow.Core.Endpoints;
using TelegramBotFlow.Core.Extensions;
using TelegramBotFlow.Core.Hosting;
using TelegramBotService.Core.Features.MainMenu.Screens;
using TelegramBotService.Core.Messaging;
using TelegramBotService.Web;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting telegram bot service");

    BotApplicationBuilder builder = BotApplication.CreateBuilder(args);

    string environment = builder.WebAppBuilder.Environment.EnvironmentName;

    builder.Configuration.AddJsonFile($"appsettings.{environment}.json", true, true);
    builder.Configuration.AddEnvironmentVariables();

    builder.WebAppBuilder.Host.UseSerilog((context, config) =>
        config.ReadFrom.Configuration(context.Configuration));

    // HTTP-proxy для outbound Telegram Bot API запросов. Нужен на проде в РФ —
    // ISP делает RST-injection на TCP-соединения к api.telegram.org (CIDR
    // 149.154.160.0/20), polling и admin-вызовы периодически таймаутят. Прокси
    // через VPS вне РФ обходит блокировку. Bot:ProxyUrl задаётся через env
    // BOT__PROXYURL (формат http://user:pass@host:port). Если не задан —
    // HttpClient ходит напрямую (dev/local окружения).
    //
    // ConfigurePrimaryHttpMessageHandler для именованного "telegram" клиента
    // (TBF регистрирует его в AddTelegramBotFlow). Дополнительные handlers
    // (TelegramRateLimitHandler, ResilienceHandler) сохраняются.
    string? proxyUrl = builder.Configuration["Bot:ProxyUrl"];
    if (!string.IsNullOrWhiteSpace(proxyUrl))
    {
        // .NET WebProxy не извлекает credentials из URI — нужно явно отдельно
        // парсить user:password и положить в WebProxy.Credentials, иначе будет
        // 407 Proxy Authentication Required.
        if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out Uri? proxyUri)
            || (!string.Equals(proxyUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(proxyUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            || string.IsNullOrWhiteSpace(proxyUri.Host))
        {
            throw new InvalidOperationException("Bot:ProxyUrl must be an absolute HTTP(S) URL.");
        }
        System.Net.WebProxy webProxy = new(
            new UriBuilder(proxyUri.Scheme, proxyUri.Host, proxyUri.Port).Uri);

        if (!string.IsNullOrEmpty(proxyUri.UserInfo))
        {
            string[] parts = proxyUri.UserInfo.Split(':', 2);
            string user = Uri.UnescapeDataString(parts[0]);
            string pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            webProxy.Credentials = new System.Net.NetworkCredential(user, pass);
        }

        builder.Services.AddHttpClient("telegram")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                Proxy = webProxy,
                UseProxy = true,
            });
        Log.Information(
            "Telegram bot HTTP proxy configured: {ProxyHost}:{ProxyPort}",
            proxyUri.Host, proxyUri.Port);
    }
    else
    {
        Log.Information("Telegram bot HTTP proxy not configured — direct connect to api.telegram.org");
    }

    // PlatformBootstrap навешивает JWT auth, CORS и rate-limiter на WebApplicationBuilder бота —
    // нужны для admin HTTP endpoint'ов (`/telegram/admin/...`) и s2s endpoint'ов. Observability
    // (traces + Serilog OTLP) отключаем — Serilog настраивается выше вручную; OpenAPI бот-сервису
    // не нужен.
    builder.WebAppBuilder.AddPlatformDefaults("TelegramBotService", o =>
    {
        o.EnableObservability = false;
        o.EnableOpenApi = false;
    });

    // Метрики при этом нужны: TelegramMetrics (issue #67) + ASP.NET/HttpClient/Runtime
    // не должны быть слепыми. Регистрируем minimal OTel pipeline только для metrics —
    // не конфликтует с TBF-овской tracing-инфраструктурой и кастомной Serilog config.
    builder.Services.AddObservabilityMetricsOnly("TelegramBotService");

    builder.Services.AddConfiguration(builder.Configuration);

    builder.WebAppBuilder.AddWolverine();

    builder.Services.AddBotEndpoints(typeof(MainMenuScreen).Assembly);
    builder.Services.AddScreens(typeof(MainMenuScreen).Assembly);

    builder.UseErrorHandling();
    builder.UseLogging();
    // ChatId discovery: ловит /chatid в группе/канале и terminates ДО PrivateChatOnly.
    builder.Use<TelegramBotService.Core.Features.ChatMember.Middleware.ChatIdQueryMiddleware>();
    builder.UsePrivateChatOnly();
    builder.UseSession();
    builder.UseAccessPolicy();
    builder.UsePendingInput();

    BotApplication app = builder.Build();

    app.SetMenu(menu => menu
        .Command("start", "Главное меню")
        .Command("help", "Справка по возможностям бота")
        .Command("unlink", "Отключить уведомления в боте"));

    app.UseNavigation<MainMenuScreen>();
    app.MapBotEndpoints();

    // HTTP pipeline + endpoint discovery (admin chat-binding endpoints, s2s).
    app.WebApp.UsePlatformDefaults();
    app.WebApp.MapEndpoints();

    // Dev-guard: если BOT__TOKEN пустой или dummy (первая группа "000000:"), TBF на старте
    // стучится в Telegram API (setMyCommands) и падает с 401 → FATAL. Чтобы локальный стек
    // поднимался без реального BotFather-токена — делаем graceful exit перед RunAsync.
    // Host к этому моменту построен, поэтому `dotnet ef migrations bundle` (EF design-time)
    // работает штатно. В prod токен всегда задан через Infisical — guard не срабатывает.
    string? rawToken = builder.Configuration["Bot:Token"];
    if (string.IsNullOrWhiteSpace(rawToken)
        || rawToken.StartsWith("000000:", StringComparison.Ordinal))
    {
        if (builder.WebAppBuilder.Environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Bot:Token is required in Production and must contain a real BotFather token.");
        }

        Log.Warning(
            "Bot token is missing or is a dev placeholder — TelegramBotService startup skipped. " +
            "Set BOT__TOKEN in .env to a real BotFather token to enable the bot.");
        return;
    }

    await app.RunAsync();
}
catch (HostAbortedException)
{
    // Expected on `dotnet ef migrations` / `dotnet ef bundle --apply` — the host
    // is aborted after the migration runs, which should NOT be logged as Fatal.
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

namespace TelegramBotService.Web
{
    public partial class Program;
}
