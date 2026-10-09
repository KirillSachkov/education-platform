using AccessService.Core.Features.PlanGrants.IntegrationEvents;
using AccessService.Domain;
using AccessService.Infrastructure.Postgres;
using ContentAccess;
using ContentAccess.Redis;
using Microsoft.EntityFrameworkCore;
using PlatformBootstrap;
using StackExchange.Redis;

namespace AccessService.Web.Configuration;

/// <summary>
/// One-shot recovery CLI: проходит по всем ACTIVE <see cref="PlanGrant"/>'ам и
/// записывает соответствующие plan-теги в Redis (<c>user-grants:{userId}</c>).
///
/// Используется когда:
/// - Wolverine self-consume handler был сломан (handler-discovery / queue binding)
///   и события <c>PlanGrantCreated</c> были опубликованы, но Redis-тег не записан.
/// - Полная потеря Redis (volume drop) — нужно перестроить grant'ы из БД.
/// - Disaster recovery после миграции / rollback'а.
///
/// Идемпотентен: SADD не дублирует. Поддерживает <c>--dry-run</c>.
///
/// Не путать с <c>migrate-enrollments-to-plans</c>: тот создаёт PlanGrant'ы
/// из legacy CourseEnrollment, а этот сводит уже-существующие PlanGrant'ы в Redis.
///
/// Usage:
///   docker exec access-service dotnet AccessService.Web.dll backfill-redis-from-grants
///   docker exec access-service dotnet AccessService.Web.dll backfill-redis-from-grants --dry-run
/// </summary>
public sealed class BackfillRedisFromGrantsCli : IPlatformCli
{
    public const string CommandName = "backfill-redis-from-grants";
    public const string DryRunFlag = "--dry-run";

    public string Name => CommandName;

    public async Task RunAsync(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        _ = hostEnvironment;
        bool dryRun = args.Any(x => string.Equals(x, DryRunFlag, StringComparison.OrdinalIgnoreCase));

        string dbConnectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database is required");
        string redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .ClearProviders()
            .SetMinimumLevel(LogLevel.Information)
            .AddSimpleConsole(options => options.SingleLine = true));
        services.AddDbContext<AccessServiceDbContext>(options => options.UseNpgsql(dbConnectionString));

        ConfigurationOptions redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.AbortOnConnectFail = false;
        IConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);
        services.AddContentAccessRedis(redis);

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();

        ILoggerFactory loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        Microsoft.Extensions.Logging.ILogger logger = loggerFactory.CreateLogger("BackfillRedisFromGrants");
        AccessServiceDbContext db = scope.ServiceProvider.GetRequiredService<AccessServiceDbContext>();
        IUserGrantWriter grantWriter = scope.ServiceProvider.GetRequiredService<IUserGrantWriter>();

        logger.LogInformation("Starting Redis backfill from PlanGrants. DryRun={DryRun}", dryRun);

        // Pull active grants joined with their plan — нужны полные Plan + PlanGrant
        // entity'и для PlanGrantTagCalculator (чтобы избежать drift между CLI и handler'ом).
        List<PlanGrant> activeGrants = await db.PlanGrants
            .AsNoTracking()
            .Where(g => g.Status == PlanGrantStatus.ACTIVE)
            .ToListAsync(cancellationToken);

        HashSet<Guid> planIds = [.. activeGrants.Select(g => g.PlanId)];
        Dictionary<Guid, Plan> plansByPlanId = await db.Plans
            .AsNoTracking()
            .Where(p => planIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        logger.LogInformation(
            "Loaded {Grants} ACTIVE plan-grants across {Plans} plans",
            activeGrants.Count, plansByPlanId.Count);

        int tagsWritten = 0;
        int usersTouched = 0;

        foreach (IGrouping<Guid, PlanGrant> userGroup in activeGrants.GroupBy(g => g.UserId))
        {
            IReadOnlyList<string> tagsForUser = PlanGrantTagCalculator.CalculateUnion(
                [.. userGroup],
                plansByPlanId);

            if (tagsForUser.Count == 0)
            {
                continue;
            }

            usersTouched++;
            tagsWritten += tagsForUser.Count;

            if (dryRun)
            {
                logger.LogInformation(
                    "[dry-run] would replace user-grants:{UserId} with {Count} tag(s): {Tags}",
                    userGroup.Key, tagsForUser.Count, string.Join(",", tagsForUser));
                continue;
            }

            // ReplaceAsync — атомарный DEL + SADD. Гарантирует чистый state без stale-тегов
            // от предыдущей повреждённой итерации.
            await grantWriter.ReplaceAsync(userGroup.Key, tagsForUser, cancellationToken);
            logger.LogInformation(
                "Replaced user-grants:{UserId} with {Count} tag(s): {Tags}",
                userGroup.Key, tagsForUser.Count, string.Join(",", tagsForUser));
        }

        logger.LogInformation(
            "Backfill complete. Users touched: {Users}, total tags written: {Tags}, dry-run: {DryRun}",
            usersTouched, tagsWritten, dryRun);
    }
}