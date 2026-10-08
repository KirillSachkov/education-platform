using CommentService.Core;
using CommentService.Infrastructure.Postgres;
using CommentService.Web.Configuration;
using ContentAccess.Redis;
using Framework.Endpoints;
using Shared.Messaging;
using StackExchange.Redis;
using CoreRegistration = CommentService.Core.Registration;

namespace CommentService.Web;

public static class Registration
{
    /// <summary>
    /// CommentService-specific DI. Cross-cutting (Serilog, OTEL, CORS, JWT,
    /// OpenAPI, endpoint discovery, middleware pipeline) comes from
    /// Shared.PlatformBootstrap.AddPlatformDefaults / UsePlatformDefaults.
    /// </summary>
    public static IServiceCollection AddCommentServiceRegistrations(this IServiceCollection services, IConfiguration configuration)
    {
        string redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
        IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisOptions);

        services.AddContentAccessRedis(redis);
        services.AddEndpoints(typeof(CoreRegistration).Assembly);

        services.AddHealthChecks()
            .AddDbContextCheck<CommentDbContext>("postgresql")
            .AddCheck("redis", new RedisHealthCheck(redis))
            .AddRabbitMqCheck(configuration);

        services.AddCommentRateLimiting();

        services
            .AddCore(configuration)
            .AddInfrastructurePostgres(configuration);

        return services;
    }
}
