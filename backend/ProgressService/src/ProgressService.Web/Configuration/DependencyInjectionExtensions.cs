using ContentAccess.Redis;
using Framework.Endpoints;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProgressService.Core;
using ProgressService.Infrastructure.Postgres;
using Shared.AI;
using Shared.AI.OpenAiCompatible;
using Shared.AI.Skills;
using Shared.Messaging;
using StackExchange.Redis;
using CoreRegistration = ProgressService.Core.DependencyInjectionExtensions;

namespace ProgressService.Web.Configuration;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// ProgressService-specific DI. Cross-cutting wiring (Serilog, OTEL, CORS,
    /// JWT auth, OpenAPI, endpoint discovery, middleware pipeline) lives in
    /// Shared.PlatformBootstrap.AddPlatformDefaults / UsePlatformDefaults.
    /// </summary>
    public static IServiceCollection AddProgressServiceRegistrations(this IServiceCollection services, IConfiguration configuration)
    {
        string redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
        IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisOptions);

        services
            .AddInfrastructurePostgres(configuration)
            .AddCore(configuration)
            .AddContentAccessRedis(redis)
            .AddEndpoints(typeof(CoreRegistration).Assembly)
            .AddProgressRateLimiting();

        // AI-грейдинг open_text ответов level-test'а (ST-5, #480). Зеркало ARS:
        // multi-provider factory (issue #146) + default-провайдер как IAiClient +
        // reusable AI Skills (handler потребляет IStructuredExtractor<T>).
        // ApiKey в Infisical / .env: AI__PROVIDERS__AITUNNEL__APIKEY (тот же, что у ARS/MPS).
        services.AddOpenAiCompatible(configuration.GetSection(AiProvidersOptions.SECTION_NAME));
        services.AddSingleton<IAiClient>(sp =>
            sp.GetRequiredService<IAiClientFactory>().Get(providerName: null));
        services.AddAiSkills();

        // Phase E (#45): legacy course-tag reconciliation удалена. Теперь Redis user-grants
        // (plan-tags) — ответственность AccessService self-consume sync handler.
        // ProgressService больше не пишет course:{id} / course:{id}:trial теги.
        // GitHub auto-enrollment перенесён на AccessService (#69) — ProgressService больше
        // не дёргает GitHub-org auto-enroll: реакция теперь у AccessService.UserGithubLoginAccessHandler,
        // который выдаёт PlanGrant'ы по матчу `plan.github_org_slug`.

        services.AddHealthChecks()
            .AddDbContextCheck<ProgressDbContext>("postgresql")
            .AddRabbitMqCheck(configuration)
            .AddCheck<RedisHealthCheck>(
                "redis",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["redis", "ready"]);

        return services;
    }
}
