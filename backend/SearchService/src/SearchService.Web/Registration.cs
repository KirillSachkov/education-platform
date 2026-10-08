using ContentAccess.Redis;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlatformAuth.Middleware;
using SearchService.Core;
using SearchService.Infrastructure.Postgres;
using SearchService.Infrastructure.Typesense;
using SearchService.Web.Configuration;
using Shared.Messaging;
using StackExchange.Redis;
using CoreRegistration = SearchService.Core.Registration;

namespace SearchService.Web;

public static class Registration
{
    /// <summary>
    /// SearchService-specific DI. Cross-cutting wiring (Serilog, OTEL, CORS,
    /// JWT auth, OpenAPI, endpoint discovery, middleware pipeline) is done by
    /// Shared.PlatformBootstrap.AddPlatformDefaults / UsePlatformDefaults.
    /// </summary>
    public static IServiceCollection AddSearchServiceRegistrations(this IServiceCollection services, IConfiguration configuration)
    {
        string redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required");
        var redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
        IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisOptions);

        services.AddContentAccessRedis(redis);
        services.AddEndpoints(typeof(CoreRegistration).Assembly);
        services.AddScoped<UserScopedData>();

        services.AddStackExchangeRedisCache(options =>
        {
            options.ConnectionMultiplexerFactory = () => Task.FromResult(redis);
            options.InstanceName = "search-service:";
        });
        services.AddHybridCache();

        services.AddSearchRateLimiting();

        services.AddHealthChecks()
            .AddDbContextCheck<SearchDbContext>("postgresql")
            .AddCheck<TypesenseHealthCheck>("typesense")
            .AddRabbitMqCheck(configuration);

        services
            .AddCore(configuration)
            .AddInfrastructurePostgres(configuration)
            .AddInfrastructureTypesense(configuration)
            .AddFileServiceHttpCommunication(configuration);

        services.TryAddSingleton(TimeProvider.System);

        // Startup auto-reindex: сравнивает SearchReindexOptions:ReindexGeneration с
        // applied_generation и публикует FullSearchReindexRequested на mismatch'е.
        // Поднимается ПОСЛЕ Wolverine HostedService — TypesenseInitializationBackgroundService
        // уже создал alias к моменту нашего publish'а.
        services.AddHostedService<SearchReindexOnStartupService>();

        // Reconciliation cron: периодический «hard rebuild» против дрейфа.
        // По умолчанию выключен (SearchReindexOptions:Reconciliation:Enabled = false) —
        // включается в production-конфиге.
        services.AddHostedService<SearchReindexReconciliationService>();

        return services;
    }
}
