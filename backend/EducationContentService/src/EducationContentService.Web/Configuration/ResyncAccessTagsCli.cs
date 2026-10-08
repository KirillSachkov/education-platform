using ContentAccess.Redis;
using EducationContentService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;

namespace EducationContentService.Web.Configuration;

/// <summary>
///     Emergency-команда для ресинхронизации Redis-тегов доступа из Postgres.
///     Используется только при disaster recovery (потеря Redis / миграция кластера /
///     обнаруженный дрейф). В нормальной работе Redis поддерживается актуальным
///     runtime event-handlers на write-path.
///
///     Usage: dotnet EducationContentService.Web.dll resync-access-tags
///
///     Процедура применения описана в <c>docs/RUNBOOK.md</c>.
/// </summary>
public static class ResyncAccessTagsCli
{
    public const string CommandName = "resync-access-tags";

    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, CommandName, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        CancellationToken cancellationToken = default)
    {
        string? connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured.");
        }

        string? redisConnection = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            throw new InvalidOperationException("Redis connection string is not configured.");
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog());
        services.AddSingleton(hostEnvironment);
        services.AddDbContext<EducationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            if (hostEnvironment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        IConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
        services.AddContentAccessRedis(redis);
        services.AddSingleton<AccessTagsResyncer>();

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();

        AccessTagsResyncer resyncer = scope.ServiceProvider.GetRequiredService<AccessTagsResyncer>();
        await resyncer.ResyncAllAsync(cancellationToken);
    }
}
