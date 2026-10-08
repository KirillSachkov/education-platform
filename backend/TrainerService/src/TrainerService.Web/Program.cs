using System.Threading.RateLimiting;
using ContentAccess.Redis;
using Framework.Endpoints;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using PlatformBootstrap;
using Shared.AI;
using Shared.AI.OpenAiCompatible;
using StackExchange.Redis;
using TrainerService.Core;
using TrainerService.Core.Features.Stats.UserLookup;
using TrainerService.Infrastructure.Postgres;
using TrainerService.Web.Configuration;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Seed taxonomy CLI (#623): `dotnet TrainerService.Web.dll seed-trainer [--force]` — idempotent
// bulk seed of tracks/topics/banks from SeedData/trainer-seed.json. Runs before AddPlatformDefaults
// so it skips the full HTTP/AI/Redis startup; resolves repositories + TransactionManager itself.
if (SeedTrainerCli.IsRequested(args))
{
    await SeedTrainerCli.RunAsync(
        builder.Configuration, builder.Environment, SeedTrainerCli.HasForceFlag(args));
    return;
}

// Free-sample backfill CLI (#674): `recompute-free-samples` re-derives per-question IsFreeSample over
// all topics. Idempotent; run post-deploy after the migration and on FreeSamplePercent changes.
if (RecomputeFreeSamplesCli.IsRequested(args))
{
    await RecomputeFreeSamplesCli.RunAsync(builder.Configuration);
    return;
}

// Daily stat-snapshot CLI (#681 T1): `snapshot-stats` runs one snapshot tick synchronously and exits —
// the same idempotent work the background job does, for tests/owner on-demand triggering.
if (SnapshotStatsCli.IsRequested(args))
{
    await SnapshotStatsCli.RunAsync(builder.Configuration);
    return;
}

// Mastery backfill CLI (#691): `recompute-mastery` re-derives every topic_masteries row from session
// history (distinct-question latest, difficulty-weighted) — fixes prod rows inflated by the old EWMA.
// Idempotent; run post-deploy. Stands before AddPlatformDefaults (no HTTP/AI/Redis startup).
if (RecomputeMasteryCli.IsRequested(args))
{
    await RecomputeMasteryCli.RunAsync(builder.Configuration);
    return;
}

builder.AddPlatformDefaults("TrainerService");

builder.Services
    .AddCore(builder.Configuration, requireFiniteProductionLimits: builder.Environment.IsProduction())
    .AddInfrastructurePostgres(builder.Configuration);

// #681 T2: re-introduce TrainerService → AuthService S2S — batch user-lookup (display name + avatar)
// used to enrich the admin stats top-AI-spenders list. Direct http://auth-service:8005 (NOT nginx —
// /internal/* isn't routed there); soft-degrades to ids only on AuthService outage.
builder.Services.AddUserLookupClient(builder.Configuration);

// Content-access entitlements (#614): TrainerService gates PAID banks / voice / mock behind the
// cap:TRAINER_PRO subscription capability via Redis SINTER. AbortOnConnectFail=false → a transient
// Redis outage degrades (ResilientEntitlementChecker fails closed) instead of crashing startup.
string? configuredRedis = builder.Configuration.GetConnectionString("Redis");
if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(configuredRedis))
    throw new InvalidOperationException("ConnectionStrings:Redis is required in production.");
string redisConnection = configuredRedis ?? "localhost:6379";
ConfigurationOptions redisOptions = ConfigurationOptions.Parse(redisConnection);
redisOptions.AbortOnConnectFail = false;
IConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);
builder.Services.AddContentAccessRedis(redis);

// AI registration (#585). Multi-provider factory; both the LLM client (open-answer grading
// + aggregate feedback) and the transcription client (Whisper STT of spoken answers) resolve
// through it. ApiKey in Infisical / .env: AI__PROVIDERS__AITUNNEL__APIKEY.
builder.Services.AddOpenAiCompatible(builder.Configuration.GetSection(AiProvidersOptions.SECTION_NAME));
builder.Services.AddSingleton<IAiClient>(sp =>
    sp.GetRequiredService<IAiClientFactory>().Get(providerName: null));
builder.Services.AddSingleton<IAiTranscriptionClient>(sp =>
    sp.GetRequiredService<IAiTranscriptionClientFactory>().Get(providerName: null));

// Health checks consumed by UsePlatformDefaults → MapPlatformHealthEndpoints.
// Postgres + Redis (cap:TRAINER_PRO entitlements); no Wolverine/RabbitMQ.
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<TrainerServiceDbContext>("postgresql")
    .AddCheck("redis", new RedisHealthCheck(redis));

// Rate-limit: voice transcription is expensive (Whisper STT). ~20 calls/min per user — anti-flood
// on re-records. Token bucket so short bursts are absorbed but sustained spam is throttled.
builder.Services.Configure<RateLimiterOptions>(options =>
{
    options.AddPolicy("trainer-transcribe", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetTokenBucketLimiter(partitionKey,
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 20,
                TokensPerPeriod = 20,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    // Admin stats dashboard: each panel hits a separate endpoint, so a single page load fans out
    // several requests. ~30/min per admin absorbs that burst while throttling scripted scraping of
    // the (Dapper-aggregate-heavy) endpoints.
    options.AddPolicy("admin-stats", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetTokenBucketLimiter(partitionKey,
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 30,
                TokensPerPeriod = 30,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
});

WebApplication app = builder.Build();

app.UsePlatformDefaults();
app.MapEndpoints();

await app.RunAsync();

namespace TrainerService.Web
{
    public sealed class Program;
}
