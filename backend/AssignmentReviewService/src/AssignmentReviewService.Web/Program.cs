using System.Threading.RateLimiting;
using AssignmentReviewService.Core;
using AssignmentReviewService.Core.Messaging;
using AssignmentReviewService.Infrastructure.GitHub;
using AssignmentReviewService.Infrastructure.Postgres;
using AuthService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.RateLimiting;
using PlatformBootstrap;
using Shared.AI;
using Shared.AI.OpenAiCompatible;
using Shared.Messaging;
using StackExchange.Redis;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddPlatformDefaults("AssignmentReviewService");

builder.Services
    .AddCore(builder.Configuration)
    .AddInfrastructurePostgres(builder.Configuration)
    .AddGitHubVcsProvider(builder.Configuration);

// #451: webhook-recovery установки GitHub App резолвит платформенного юзера по GitHub
// external id через AuthService (internal /by-github-id, под service-токеном). Нужен,
// когда install-callback не отработал и installation.created остаётся без VcsInstallation.
builder.Services.AddAuthServiceHttpCommunication(builder.Configuration);

// AI registration. Multi-provider factory (issue #146). AiReviewer резолвит клиента
// через factory. ApiKey в Infisical / .env: AI__PROVIDERS__AITUNNEL__APIKEY.
// IAiEmbeddingsClient больше не регистрируется — RAG-pipeline удалён (#320).
builder.Services.AddOpenAiCompatible(builder.Configuration.GetSection(AiProvidersOptions.SECTION_NAME));
builder.Services.AddSingleton<IAiClient>(sp =>
    sp.GetRequiredService<IAiClientFactory>().Get(providerName: null));

// Redis conditional registration — Phase 4 install-state-store использует Redis
// в multi-instance prod (StringGetDeleteAsync — атомарный single-use). В Testing
// env Redis скипается и Core.Registration fallback'ит на InMemoryInstallStateStore.
if (!builder.Environment.IsEnvironment("Testing"))
{
    string? redisConnection = builder.Configuration.GetConnectionString("Redis");
    if (!string.IsNullOrEmpty(redisConnection))
    {
        ConfigurationOptions redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
        IConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);
        builder.Services.AddSingleton(redis);
    }
}

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AssignmentReviewServiceDbContext>("postgresql")
    .AddRabbitMqCheck(builder.Configuration);

// Phase 4 rate-limit policies.
builder.Services.Configure<RateLimiterOptions>(options =>
{
    // Install-start: 10 calls / 5 min per authenticated user — anti-flood
    // Connect-button (юзер мог кликнуть много раз).
    options.AddPolicy("ar-github-install-start", httpContext =>
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

    // GitHub webhook: 100 / min per IP (GitHub IPs only). DDoS guard для злоумышленника
    // знающего secret (theoretical edge case).
    options.AddPolicy("ar-github-webhook", httpContext =>
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

    // Phase 7 (#15): run-iteration burst guard — 5 запусков AI-проверки в минуту
    // на пользователя. Per-user / per-submission daily caps реализованы
    // domain-level в RateLimitChecker (cost guard, не burst).
    options.AddPolicy("ar-review-iteration", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });

    // #713 (1b): reply-to-student burst guard — 20 ответов в минуту на автора. Каждый
    // ответ постит коммент в GitHub-тред PR (внешний write), поэтому anti-flood обязателен.
    options.AddPolicy("ar-student-reply", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
});

builder.AddWolverine();

WebApplication app = builder.Build();

app.UsePlatformDefaults();
app.MapEndpoints();

await app.RunAsync();

namespace AssignmentReviewService.Web
{
    public sealed class Program;
}
