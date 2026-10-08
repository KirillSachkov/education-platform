using ContentAccess.Redis;
using PlatformBootstrap;
using StackExchange.Redis;

namespace AccessService.Web.Configuration;

/// <summary>
/// Issue #358 cleanup CLI: removes stale <c>plan:free:author_*</c> tags from all
/// <c>user-grants:*</c> Redis sets. Run after the data migration archives FREE plans
/// and revokes AUTO_FREE grants — the grant-revoke handler isn't triggered by the
/// raw SQL UPDATE, so the Redis tag side stays stale until this script runs.
///
/// Idempotent: SREM is a no-op for tags that aren't present, so re-running is safe.
/// Streams keys via SCAN (cursor-based) to avoid blocking Redis on large keyspaces.
///
/// Usage:
///   docker exec access-service dotnet AccessService.Web.dll cleanup-free-tags
///   docker exec access-service dotnet AccessService.Web.dll cleanup-free-tags --dry-run
/// </summary>
public sealed class CleanupFreeTagsCli : IPlatformCli
{
    public const string CommandName = "cleanup-free-tags";
    public const string DryRunFlag = "--dry-run";

    // Legacy tag prefix removed from GrantTags by #358. Hard-coded here because
    // it's a one-shot recovery script and we don't want to keep the constant
    // around in production code just for this.
    private const string LegacyFreeTagPrefix = "plan:free:author_";

    private const string UserGrantsKeyPattern = "user-grants:*";

    public string Name => CommandName;

    public async Task RunAsync(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        _ = hostEnvironment;
        bool dryRun = args.Any(x => string.Equals(x, DryRunFlag, StringComparison.OrdinalIgnoreCase));

        string redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .ClearProviders()
            .SetMinimumLevel(LogLevel.Information)
            .AddSimpleConsole(options => options.SingleLine = true));

        ConfigurationOptions redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.AbortOnConnectFail = false;
        IConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        Microsoft.Extensions.Logging.ILogger logger = loggerFactory.CreateLogger("CleanupFreeTags");

        logger.LogInformation("Starting Redis cleanup of plan:free:author_* tags. DryRun={DryRun}", dryRun);

        IDatabase db = redis.GetDatabase();
        long usersTouched = 0;
        long tagsRemoved = 0;

        // SCAN всех endpoint'ов — обычно у нас один master, но цикл по всем для робастности.
        foreach (System.Net.EndPoint endpoint in redis.GetEndPoints())
        {
            IServer server = redis.GetServer(endpoint);
            if (server.IsReplica)
            {
                continue;
            }

            await foreach (RedisKey key in server.KeysAsync(pattern: UserGrantsKeyPattern, pageSize: 500).WithCancellation(cancellationToken))
            {
                List<RedisValue> tagsToRemove = [];
                await foreach (RedisValue member in db.SetScanAsync(key, pattern: $"{LegacyFreeTagPrefix}*", pageSize: 100).WithCancellation(cancellationToken))
                {
                    tagsToRemove.Add(member);
                }

                if (tagsToRemove.Count == 0)
                {
                    continue;
                }

                usersTouched++;
                tagsRemoved += tagsToRemove.Count;

                if (dryRun)
                {
                    logger.LogInformation(
                        "[dry-run] would SREM {Count} legacy tag(s) from {Key}: {Tags}",
                        tagsToRemove.Count, (string)key!, string.Join(",", tagsToRemove.Select(t => (string)t!)));
                    continue;
                }

                long removed = await db.SetRemoveAsync(key, [.. tagsToRemove]);
                logger.LogInformation(
                    "Removed {Removed} legacy tag(s) from {Key}: {Tags}",
                    removed, (string)key!, string.Join(",", tagsToRemove.Select(t => (string)t!)));
            }
        }

        logger.LogInformation(
            "Cleanup complete. Users touched: {Users}, total tags removed: {Tags}, dry-run: {DryRun}",
            usersTouched, tagsRemoved, dryRun);

        // Подсказка для последующего реиндекса search-документов: после удаления FREE
        // плана у автора search-снапшоты могут содержать stale `plan:free:author_*`
        // в required_access_tags до следующего реиндекса (см. SearchReindexOptions bump).
        _ = EntitlementKeys.UserGrants(Guid.Empty); // sanity-touch — гарантирует, что namespace используется на build.
    }
}
