using ContentAccess.Redis;
using EducationContentService.Core;
using EducationContentService.Infrastructure.Postgres;
using Framework.Endpoints;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shared.Messaging;
using StackExchange.Redis;
using CoreRegistration = EducationContentService.Core.DependencyInjectionExtensions;

namespace EducationContentService.Web.Configuration;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// EducationContentService-specific DI. Cross-cutting (Serilog, OTEL, CORS,
    /// JWT, OpenAPI, endpoint discovery, pipeline) comes from
    /// Shared.PlatformBootstrap.AddPlatformDefaults / UsePlatformDefaults.
    /// </summary>
    public static IServiceCollection AddEducationContentServiceRegistrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required");
        var redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
        IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisOptions);

        services.AddContentAccessRedis(redis);
        services.AddEndpoints(typeof(CoreRegistration).Assembly);

        services.AddHealthChecks()
            .AddDbContextCheck<EducationDbContext>("postgresql")
            .AddRabbitMqCheck(configuration)
            .AddCheck<RedisHealthCheck>(
                "redis",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["redis", "ready"]);

        services
            .AddCore(configuration)
            .AddInfrastructurePostgres(configuration)
            .AddEducationRateLimiting();

        services.AddSingleton<AccessTagsResyncer>();

        return services;
    }
}
