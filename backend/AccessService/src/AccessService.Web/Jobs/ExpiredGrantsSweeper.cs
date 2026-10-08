using AccessService.Core.Database;
using AccessService.Domain;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace AccessService.Web.Jobs;

/// <summary>
/// Periodically scans <see cref="PlanGrant"/> rows where <c>Status = ACTIVE</c> and
/// effective access boundary is elapsed. Enabled recurring grants have a hard fallback at
/// <c>ExpiresAt + 72h</c> even when the first billing attempt was missed; cancelled/refunded
/// and non-recurring grants end at <c>ExpiresAt</c>. Calls
/// <see cref="PlanGrant.Expire"/> and publishes
/// <see cref="PlanGrantExpired"/> integration event so consumers (Redis sync, Telegram
/// notifications, etc.) can clean up.
///
/// Не используется Redis-side EXPIRE — gradient'а доступа на уровне отдельных tag'ов
/// в `usergrants:{userId}` не существует (TTL применим только к ключу целиком).
/// Period: <c>SweepInterval</c> (default 5 min). Lookahead: <c>ExpiryGracePeriod</c>
/// (default 0 — захватываем строго истёкшие). Batch size: <c>BatchSize</c> (default 200).
/// </summary>
public sealed class ExpiredGrantsSweeper : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<ExpiredGrantsSweeperOptions> _options;
    private readonly ILogger<ExpiredGrantsSweeper> _logger;

    public ExpiredGrantsSweeper(
        IServiceProvider services,
        IOptionsMonitor<ExpiredGrantsSweeperOptions> options,
        ILogger<ExpiredGrantsSweeper> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay so the host can come up fully before we touch the DB.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExpiredGrantsSweeper iteration failed; will retry next tick");
            }

            try
            {
                await Task.Delay(_options.CurrentValue.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        ExpiredGrantsSweeperOptions opts = _options.CurrentValue;
        DateTimeOffset cutoff = DateTimeOffset.UtcNow + opts.ExpiryGracePeriod;
        int batchSize = Math.Max(1, opts.BatchSize);

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IPlanGrantsRepository grants = scope.ServiceProvider.GetRequiredService<IPlanGrantsRepository>();
        IPlansRepository plans = scope.ServiceProvider.GetRequiredService<IPlansRepository>();

        // Bounded LIMIT in SQL — avoids loading an unbounded result set on a grant storm.
        IReadOnlyList<PlanGrant> batch = await grants.GetEffectiveExpiringBatchAsync(
            cutoff,
            batchSize,
            cancellationToken);

        if (batch.Count == 0)
        {
            return 0;
        }

        // Batch-fetch all distinct plans up front to avoid an N+1 lookup inside the loop.
        Guid[] planIds = batch.Select(g => g.PlanId).Distinct().ToArray();
        IReadOnlyList<Plan> planRows = await plans.GetManyByAsync(p => planIds.Contains(p.Id), cancellationToken);
        Dictionary<Guid, Plan> planCache = planRows.ToDictionary(p => p.Id);
        Guid[] trialAuthorIds = planRows
            .Where(plan => plan.IsTrial)
            .Select(plan => plan.AuthorId)
            .Distinct()
            .ToArray();
        IReadOnlyList<Plan> canonicalCandidates = trialAuthorIds.Length == 0
            ? []
            : await plans.GetManyByAsync(
                plan => trialAuthorIds.Contains(plan.AuthorId)
                        && plan.Tier == PlanTier.FULL_ALL
                        && plan.TrialDurationDays == null
                        && plan.ArchivedAt == null
                        && plan.IsActive
                        && plan.IsPublic,
                cancellationToken);
        Dictionary<Guid, Guid> canonicalByAuthorId = canonicalCandidates
            .GroupBy(plan => plan.AuthorId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(plan => plan.IsHighlighted)
                    .ThenBy(plan => plan.DisplayOrder)
                    .ThenBy(plan => plan.CreatedAt)
                    .ThenBy(plan => plan.Id)
                    .First()
                    .Id);
        int expired = 0;

        foreach (PlanGrant grant in batch)
        {
            if (!planCache.TryGetValue(grant.PlanId, out Plan? plan))
            {
                _logger.LogWarning(
                    "Plan {PlanId} not found while expiring grant {GrantId}; skipping",
                    grant.PlanId, grant.Id);
                continue;
            }

            Guid? canonicalTelegramPlanId = plan.IsTrial
                ? canonicalByAuthorId.GetValueOrDefault(plan.AuthorId)
                : plan.Id;

            if (await ExpireOneInScopeAsync(
                    grant.Id,
                    cutoff,
                    plan,
                    canonicalTelegramPlanId,
                    cancellationToken))
            {
                expired++;
            }
        }

        if (expired > 0)
        {
            _logger.LogInformation(
                "ExpiredGrantsSweeper transitioned {Expired} grant(s) to EXPIRED (cutoff={Cutoff:O})",
                expired, cutoff);
        }

        return expired;
    }

    private async Task<bool> ExpireOneInScopeAsync(
        Guid grantId,
        DateTimeOffset cutoff,
        Plan plan,
        Guid? canonicalTelegramPlanId,
        CancellationToken ct)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        IOrdersRepository orders = services.GetRequiredService<IOrdersRepository>();
        IAsyncDisposable? renewalLock = await orders.TryAcquireRenewalLockAsync(grantId, ct);
        if (renewalLock is null)
        {
            _logger.LogDebug(
                "ExpiredGrantsSweeper skipped grant {GrantId}: renewal transition is in progress",
                grantId);
            return false;
        }

        await using (renewalLock)
        {
            IPlanGrantsRepository grants = services.GetRequiredService<IPlanGrantsRepository>();
            Result<PlanGrant, Error> grantResult = await grants.GetByAsync(g => g.Id == grantId, ct);
            if (grantResult.IsFailure)
            {
                return false;
            }

            PlanGrant grant = grantResult.Value;
            if (grant.Status != PlanGrantStatus.ACTIVE
                || grant.AccessEndsAt is null
                || grant.AccessEndsAt > cutoff)
            {
                return false;
            }

            UnitResult<Error> expire = grant.Expire();
            if (expire.IsFailure)
            {
                _logger.LogDebug(
                    "Skip grant {GrantId}: not active ({Error})", grant.Id, expire.Error.Type);
                return false;
            }

            IOutboxService outbox = services.GetRequiredService<IOutboxService>();
            await outbox.PublishAsync(new PlanGrantExpired(
                grant.Id,
                grant.UserId,
                grant.PlanId,
                plan.Tier.ToString(),
                plan.AuthorId,
                plan.FirstCourseId,
                DateTimeOffset.UtcNow,
                [.. plan.Courses.Select(c => c.CourseId)],
                canonicalTelegramPlanId));

            ITransactionManager transactions = services.GetRequiredService<ITransactionManager>();
            UnitResult<Error> save = await transactions.SaveChangesAsync(ct);
            if (save.IsFailure)
            {
                _logger.LogError(
                    "ExpiredGrantsSweeper failed to persist grant {GrantId}: {Error}",
                    grant.Id,
                    save.Error.Type);
                return false;
            }

            return true;
        }
    }
}

public sealed class ExpiredGrantsSweeperOptions
{
    public const string SectionName = "ExpiredGrantsSweeper";

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan ExpiryGracePeriod { get; set; } = TimeSpan.Zero;

    public int BatchSize { get; set; } = 200;
}
