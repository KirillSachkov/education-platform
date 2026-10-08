using ContentAccess.Redis;
using EducationContentService.Contracts.HttpCommunication;
using FileService.Core;
using FileService.Infrastructure.Imaging;
using FileService.Infrastructure.Kinescope;
using FileService.Infrastructure.Postgres;
using FileService.Infrastructure.S3;
using FileService.Web.Jobs;
using Framework.Endpoints;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shared.Messaging;
using StackExchange.Redis;
using CoreRegistration = FileService.Core.Registration;

namespace FileService.Web.Configuration;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// FileService-specific DI. Cross-cutting wiring (Serilog, OTEL, CORS,
    /// JWT auth, OpenAPI, endpoint discovery, middleware pipeline) comes from
    /// Shared.PlatformBootstrap.AddPlatformDefaults / UsePlatformDefaults.
    /// </summary>
    public static IServiceCollection AddFileServiceRegistrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers();

        string redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required");
        var redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
        IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisOptions);

        services
            .AddEducationServiceHttpCommunication(configuration)
            .AddContentAccessRedis(redis)
            .AddEndpoints(typeof(CoreRegistration).Assembly)
            .AddS3(configuration)
            .AddKinescope(configuration);

        services.AddHealthChecks()
            .AddDbContextCheck<FileServiceDbContext>("postgresql")
            .AddCheck<RedisHealthCheck>(
                "redis",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["redis", "ready"])
            .AddCheck<S3HealthCheck>(
                "s3",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["s3", "ready"])
            .AddCheck<KinescopeHealthCheck>(
                "kinescope",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["kinescope", "ready"])
            .AddRabbitMqCheck(configuration);

        services.AddStackExchangeRedisCache(options =>
        {
            options.ConnectionMultiplexerFactory = () => Task.FromResult(redis);
            options.InstanceName = "file-service:";
        });

        services
            .AddCore(configuration)
            .AddInfrastructurePostgres(configuration)
            .AddImaging()
            .AddFileRateLimiting();

        services.AddHostedService<FileServiceBackgroundJobs>();

        return services;
    }
}
