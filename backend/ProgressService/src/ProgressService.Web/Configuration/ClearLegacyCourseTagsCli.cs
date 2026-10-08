using Serilog;
using StackExchange.Redis;

namespace ProgressService.Web.Configuration;

/// <summary>
///     Phase E one-shot Redis cleanup CLI.
///     Проходит по всем <c>usergrants:{userId}</c> множествам и удаляет тэги
///     с префиксами <c>course:</c> (включая <c>course:{id}:trial</c>).
///     Резервный метод после переключения read path на plan-only теги — даже
///     если новые тэги уже стали primary через `ContentAccessTagBuilder`,
///     legacy course-теги остаются в `usergrants` set'ах и занимают память.
///
///     Идемпотентен — безопасно перезапускать. <c>--dry-run</c> для оценки
///     объёма без записи. Скан использует <c>SCAN</c> + <c>SMEMBERS</c>, не
///     блокирует Redis (ВMOVE по чанкам).
///
///     Usage:
///         dotnet ProgressService.Web.dll clear-legacy-course-tags
///         dotnet ProgressService.Web.dll clear-legacy-course-tags --dry-run
///
///     Процедура: см. <c>docs/RUNBOOK.md</c> Phase 9/E.
/// </summary>
public static class ClearLegacyCourseTagsCli
{
    public const string CommandName = "clear-legacy-course-tags";
    public const string DryRunFlag = "--dry-run";

    private const string CoursePrefix = "course:";
    private const string UserGrantsPrefix = "usergrants:";

    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, CommandName, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        _ = hostEnvironment;
        bool dryRun = args.Any(x => string.Equals(x, DryRunFlag, StringComparison.OrdinalIgnoreCase));

        string? redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required");

        ConfigurationOptions options = ConfigurationOptions.Parse(redisConnection);
        options.AbortOnConnectFail = false;
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(options);

        Microsoft.Extensions.Logging.ILoggerFactory loggerFactory =
            new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger, dispose: false);
        Microsoft.Extensions.Logging.ILogger logger = loggerFactory.CreateLogger("ClearLegacyCourseTags");

        IDatabase db = redis.GetDatabase();
        IServer server = redis.GetServer(redis.GetEndPoints()[0]);

        long usersTouched = 0;
        long tagsRemovedTotal = 0;

        logger.LogInformation(
            "Starting legacy course-tag cleanup. DryRun={DryRun}", dryRun);

        await foreach (RedisKey key in server.KeysAsync(pattern: $"{UserGrantsPrefix}*").WithCancellation(cancellationToken))
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
                    "[dry-run] Would remove {Count} legacy course-tag(s) from {Key}",
                    legacyTags.Count, key);
                continue;
            }

            await db.SetRemoveAsync(key, [.. legacyTags]);
            logger.LogInformation(
                "Removed {Count} legacy course-tag(s) from {Key}", legacyTags.Count, key);
        }

        logger.LogInformation(
            "Legacy course-tag cleanup complete. Users touched: {Users}, tags removed: {Tags}{DryRunSuffix}",
            usersTouched, tagsRemovedTotal, dryRun ? " (dry-run — nothing persisted)" : "");
    }
}
