using System.Threading.RateLimiting;
using ContentAccess.Redis;
using Framework.Endpoints;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PlatformBootstrap;
using AccessService.Core;
using AccessService.Core.Features.Billing.Reconciliation;
using AccessService.Core.Messaging;
using AccessService.Infrastructure.Postgres;
using AccessService.Web.Configuration;
using AccessService.Web.Jobs;
using AuthService.Contracts.HttpCommunication;
using EducationContentService.Contracts.HttpCommunication;
using TelegramBotService.Contracts.HttpCommunication;
using Shared.Messaging;
using StackExchange.Redis;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// CLI commands — bypass the web host entirely.
if (await PlatformCli.TryRunAsync(builder, args, [
    new BackfillRedisFromGrantsCli(),
    new CleanupFreeTagsCli(),
]))
{
    return;
}

builder.AddPlatformDefaults("AccessService");

builder.Services
    .AddCore(builder.Configuration)
    .AddInfrastructurePostgres(builder.Configuration)
    .AddPaymentsCore(builder.Configuration)
    .AddTBankClient()
    .AddAuthServiceHttpCommunication(builder.Configuration)
    .AddEducationServiceHttpCommunication(builder.Configuration, enableCaching: true)
    .AddTelegramBotServiceHttpCommunication(builder.Configuration);

// Redis registration is skipped in Testing — the integration test factory wires a
// FakeUserGrantWriter directly. In Development / Docker / Production we connect to
// the real Redis instance.
if (!builder.Environment.IsEnvironment("Testing"))
{
    string redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    ConfigurationOptions redisOptions = ConfigurationOptions.Parse(redisConnection);
    redisOptions.AbortOnConnectFail = false;
    IConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);

    builder.Services.AddContentAccessRedis(redis);
}

IHealthChecksBuilder healthChecks = builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AccessServiceDbContext>("postgresql")
    .AddRabbitMqCheck(builder.Configuration);

if (!builder.Environment.IsEnvironment("Testing"))
{
    healthChecks.AddCheck<RedisHealthCheck>(
        "redis",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["redis", "ready"]);
}

// Endpoint-specific rate limits. Policies partition by user when authenticated and
// fall back to the remote IP for anonymous traffic.
builder.Services.Configure<RateLimiterOptions>(options =>
{
    options.AddPolicy("user-lookup", httpContext =>
    {
        string partitionKey = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst("sub")?.Value
              ?? httpContext.User.Identity.Name
              ?? httpContext.Connection.RemoteIpAddress?.ToString()
              ?? "anon"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });

    // Public catalog/config endpoints execute database queries and, for the pricing
    // catalog, may also call ECS. Bound anonymous scraping without affecting normal UI use.
    options.AddPolicy("anonymous-read", httpContext =>
    {
        string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });

    // GitHub App install-redirect — author-only, защищаем от accidental flood
    // (юзер кликает многократно). Низкий лимит — install это редкая операция.
    options.AddPolicy("github-app-install", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            });
    });

    // GitHub invitation create — student-side. Защита от spam'а GitHub API
    // (rate limits на их стороне) + accidental retry в onboarding wizard.
    options.AddPolicy("github-app-invitation", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
            });
    });

    // Webhook от GitHub — partition по IP (GitHub IPs только), guard от
    // attacker'а с валидным webhook secret (theoretical edge case).
    options.AddPolicy("github-webhook", httpContext =>
    {
        string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 50,
            });
    });

    // Phase 2 #112 — upgrade-quote endpoint. Каждый вызов делает full-table
    // GetManyByAsync(g => g.UserId == ...) + IN-query на planIds. Guard от scraping'а
    // pricing-страницы / loop-вызова на десятки планов. 60 req/min per user
    // достаточно для UI (4-6 планов на страницу × 1 mount + retry).
    options.AddPolicy("upgrade-quote", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            });
    });

    // Phase F.1 #124 — POST /access/orders/. Каждый вызов открывает заказ +
    // дёргает T-Bank Init API. Без лимита злоумышленный авторизованный юзер
    // может в цикле создавать PENDING заказы → саморасход T-Bank API quota
    // + засорение orders. 10 req/min per sub достаточно для UI (1 click + retry).
    options.AddPolicy("order-create", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });

    // Phase F.1 #124 — POST /access/webhooks/tbank/. Anonymous endpoint,
    // защищён только signature. Лимит per IP — defense-in-depth от floода
    // invalid-signature пайл-апов (каждый hit читает body, парсит JSON,
    // ходит в БД за Order'ом). T-Bank сам шлёт максимум ~1 webhook/sec на
    // заказ при retry'ях.
    options.AddPolicy("tbank-webhook", httpContext =>
    {
        string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 30,
            });
    });

    // Issue #228 SEC-1 — POST /access/invites/{token}/redeem. Authenticated, но
    // открыт всем пользователям. Без лимита авторизованный (или compromised)
    // юзер может в цикле трепать redeem против известных invite-ID — каждый
    // запрос читает invite + plan, чек grant existing, и при удаче пишет
    // 3 строки. 10 req/min на sub достаточно: реальный flow — 1 клик после
    // landing'а. Token space (~131 бит энтропии) исключает enumeration; лимит
    // защищает от DoS-амплификации.
    options.AddPolicy("invite-redeem", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });

});

builder.AddWolverine();

builder.Services.Configure<ExpiredGrantsSweeperOptions>(
    builder.Configuration.GetSection(ExpiredGrantsSweeperOptions.SectionName));
builder.Services.Configure<TrialExpiryReminderSweeperOptions>(
    builder.Configuration.GetSection(TrialExpiryReminderSweeperOptions.SectionName));
builder.Services.Configure<RecurringChargesSweeperOptions>(
    builder.Configuration.GetSection(RecurringChargesSweeperOptions.SectionName));
builder.Services.Configure<TgJoinReminderSweeperOptions>(
    builder.Configuration.GetSection(TgJoinReminderSweeperOptions.SectionName));
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<ExpiredGrantsSweeper>();
    builder.Services.AddHostedService<TrialExpiryReminderSweeper>();
    builder.Services.AddHostedService<TgJoinReminderSweeper>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PendingOrderReconciliationService>());

    // Recurring-subscription auto-renewal (#614) — gated by its own Enabled flag so it can
    // be toggled off independently without touching the other sweepers.
    RecurringChargesSweeperOptions recurringOptions =
        builder.Configuration.GetSection(RecurringChargesSweeperOptions.SectionName)
            .Get<RecurringChargesSweeperOptions>() ?? new RecurringChargesSweeperOptions();
    if (recurringOptions.Enabled)
    {
        builder.Services.AddHostedService<RecurringChargesSweeper>();
    }
}

WebApplication app = builder.Build();

app.UsePlatformDefaults();
app.MapEndpoints();

await app.RunAsync();

namespace AccessService.Web
{
    public sealed class Program;
}
