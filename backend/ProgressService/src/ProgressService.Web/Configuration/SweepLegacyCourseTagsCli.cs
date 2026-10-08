using PlatformBootstrap;
using StackExchange.Redis;

namespace ProgressService.Web.Configuration;

/// <summary>
///     Phase E one-shot Redis sweep CLI.
///
///     После Phase E (#45) write-path больше не публикует legacy <c>course:{id}</c> /
///     <c>course:{id}:trial</c> теги при enrollment'е — entitlement layer полностью
///     перешёл на plan-теги (<c>plan:lifetime:author_X</c>, <c>plan:course:{id}</c>,
///     <c>plan:free:author_X</c>). Однако в Redis ещё остались исторические course-теги
///     от записей до cutover'а — они занимают память в <c>usergrants:{userId}</c> set'ах,
///     никогда больше не используются для access-check'ов и должны быть sweep'нуты.
///
///     Поведение:
///     - SCAN по pattern <c>usergrants:*</c>
///     - SMEMBERS на каждый user set, фильтрация по префиксу <c>course:</c>
///     - SREM найденных тегов (один pipeline'd вызов на user)
///     - <c>plan:*</c> теги НЕ ТРОГАЕМ — они каноничные.
///     - Идемпотентен — повторный run на чистом Redis no-op.
///     - <c>--dry-run</c> для оценки объёма без записи.
///
///     Usage:
///         docker exec progress-service dotnet ProgressService.Web.dll sweep-legacy-course-tags
///         docker exec progress-service dotnet ProgressService.Web.dll sweep-legacy-course-tags --dry-run
///
///     Процедура: см. <c>docs/RUNBOOK.md</c> Phase 9.
/// </summary>
public sealed class SweepLegacyCourseTagsCli : IPlatformCli
{
    public const string CommandName = "sweep-legacy-course-tags";
    public const string DryRunFlag = "--dry-run";

    private const string CoursePrefix = "course:";
    private const string UserGrantsPrefix = "usergrants:";

    public string Name => CommandName;

    public async Task RunAsync(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        _ = hostEnvironment;
        bool dryRun = args.Any(x => string.Equals(x, DryRunFlag, StringComparison.OrdinalIgnoreCase));

        string redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required");

        ConfigurationOptions options = ConfigurationOptions.Parse(redisConnection);
        options.AbortOnConnectFail = false;

        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(options);

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
            .ClearProviders()
            .SetMinimumLevel(LogLevel.Information)
            .AddSimpleConsole(o => o.SingleLine = true));
        Microsoft.Extensions.Logging.ILogger logger = loggerFactory.CreateLogger("SweepLegacyCourseTags");

        IDatabase db = redis.GetDatabase();
        IServer server = redis.GetServer(redis.GetEndPoints()[0]);

        long usersTouched = 0;
        long tagsRemovedTotal = 0;

        logger.LogInformation(
            "Starting legacy course-tag sweep. DryRun={DryRun}", dryRun);

        await foreach (RedisKey key in server
            .KeysAsync(pattern: $"{UserGrantsPrefix}*")
            .WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            RedisValue[] members = await db.SetMembersAsync(key);
            if (members.Length == 0)
            {
                continue;
            }

            List<RedisValue> legacyTags = [];
            foreach (RedisValue member in members)
            {
                string? value = member.ToString();
                if (!string.IsNullOrEmpty(value)
                    && value.StartsWith(CoursePrefix, StringComparison.Ordinal))
                {
                    legacyTags.Add(member);
                }
            }

            if (legacyTags.Count == 0)
            {
                continue;
            }

            usersTouched++;
            tagsRemovedTotal += legacyTags.Count;

            if (dryRun)
            {
                logger.LogInformation(
                    "[dry-run] Would remove {Count} legacy course-tag(s) from {Key}: {Tags}",
                    legacyTags.Count, key, string.Join(",", legacyTags));
                continue;
            }

            await db.SetRemoveAsync(key, [.. legacyTags]);
            logger.LogInformation(
                "Removed {Count} legacy course-tag(s) from {Key}", legacyTags.Count, key);
        }

        logger.LogInformation(
            "Legacy course-tag sweep complete. Users touched: {Users}, tags removed: {Tags}{DryRunSuffix}",
            usersTouched, tagsRemovedTotal, dryRun ? " (dry-run — nothing persisted)" : "");
    }
}
