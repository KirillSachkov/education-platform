using Framework.Endpoints;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shared.Messaging;
using StackExchange.Redis;
using TagService.Core;
using TagService.Infrastructure.Postgres;
using TagService.Web.Configuration;

namespace TagService.Web;

public static class Registration
{
    /// <summary>
    /// TagService-specific DI — everything cross-cutting (Serilog, OTEL, CORS,
    /// JWT auth, OpenAPI, endpoint discovery, middleware pipeline) is wired by
    /// Shared.PlatformBootstrap.AddPlatformDefaults / UsePlatformDefaults.
    /// </summary>
    public static IServiceCollection AddTagServiceRegistrations(this IServiceCollection services, IConfiguration configuration)
    {
        string redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
        Task<ConnectionMultiplexer> redisConnectionTask = ConnectionMultiplexer.ConnectAsync(redisOptions);

        services.AddSingleton<IConnectionMultiplexer>(_ => redisConnectionTask.GetAwaiter().GetResult());
        services.AddEndpoints(typeof(TagService.Core.Registration).Assembly);
        services.AddHybridCache();
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConnectionMultiplexerFactory = async () => await redisConnectionTask;
            options.InstanceName = "tag-service:";
        });

        services.AddTagRateLimiting();

        services
            .AddCore(configuration)
            .AddInfrastructurePostgres(configuration);

        services.AddHealthChecks()
            .AddDbContextCheck<TagDbContext>("postgresql")
            .AddRabbitMqCheck(configuration)
            .AddCheck<RedisHealthCheck>(
                "redis",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["redis", "ready"]);

        return services;
    }
}
