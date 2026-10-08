using AccessService.Core.Database;
using AccessService.Domain;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace AccessService.Web.Jobs;

/// <summary>
/// Periodically scans ACTIVE <see cref="PlanGrant"/> rows whose <c>ExpiresAt</c> falls
/// inside the next <c>ReminderLeadDays</c> and that have not yet been reminded
/// (<c>ExpiryReminderSentAt == null</c>). For each grant whose plan is a trial plan
/// (<see cref="Plan.IsTrial"/>) it publishes <see cref="TrialExpiryApproaching"/> so
/// consumers can notify the user that the trial is about to end, then stamps
/// <see cref="PlanGrant.MarkReminderSent"/> for idempotency (#580).
///
/// Period: <c>SweepInterval</c> (default 1h). Lead: <c>ReminderLeadDays</c> (default 7, #687).
/// Hosted only in non-Testing environments; integration tests instantiate it directly.
/// </summary>
public sealed class TrialExpiryReminderSweeper : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<TrialExpiryReminderSweeperOptions> _options;
    private readonly ILogger<TrialExpiryReminderSweeper> _logger;

    public TrialExpiryReminderSweeper(
        IServiceProvider services,
        IOptionsMonitor<TrialExpiryReminderSweeperOptions> options,
        ILogger<TrialExpiryReminderSweeper> logger)
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
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
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
                _logger.LogError(ex, "TrialExpiryReminderSweeper iteration failed; will retry next tick");
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
        TrialExpiryReminderSweeperOptions opts = _options.CurrentValue;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset windowEnd = now + TimeSpan.FromDays(Math.Max(1, opts.ReminderLeadDays));
        int batchSize = Math.Max(1, opts.BatchSize);

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IPlanGrantsRepository grants = scope.ServiceProvider.GetRequiredService<IPlanGrantsRepository>();
        IPlansRepository plans = scope.ServiceProvider.GetRequiredService<IPlansRepository>();
        IOutboxService outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        ITransactionManager transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        // Bounded LIMIT in SQL — avoids loading an unbounded result set on a grant storm.
        IReadOnlyList<PlanGrant> batch = await grants.GetExpiringBatchAsync(
            g => g.Status == PlanGrantStatus.ACTIVE
                 && g.ExpiresAt != null
                 && g.ExpiresAt > now
                 && g.ExpiresAt <= windowEnd
                 && g.ExpiryReminderSentAt == null,
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
        int reminded = 0;

        foreach (PlanGrant grant in batch)
        {
            if (!planCache.TryGetValue(grant.PlanId, out Plan? plan))
            {
                _logger.LogWarning(
                    "Plan {PlanId} not found while reminding trial grant {GrantId}; skipping",
                    grant.PlanId, grant.Id);
                continue;
            }

            // Only trial grants get the expiry reminder — non-trial TTL grants are out of scope.
            if (!plan.IsTrial)
            {
                continue;
            }

            await outbox.PublishAsync(new TrialExpiryApproaching(
                grant.Id,
                grant.UserId,
                plan.Id,
                plan.AuthorId,
                plan.DisplayName.Value,
                grant.ExpiresAt!.Value));

            grant.MarkReminderSent(now);
            reminded++;
        }

        UnitResult<Error> save = await transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            _logger.LogError(
                "TrialExpiryReminderSweeper failed to persist {Count} reminders: {Error}",
                reminded, save.Error.Type);
            return 0;
        }

        if (reminded > 0)
        {
            _logger.LogInformation(
                "TrialExpiryReminderSweeper published {Reminded} trial-expiry reminder(s) (window={WindowEnd:O})",
                reminded, windowEnd);
        }

        return reminded;
    }
}

public sealed class TrialExpiryReminderSweeperOptions
{
    public const string SectionName = "TrialExpiryReminderSweeper";

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);

    // #687: owner хочет напоминание «за неделю» до истечения месячного доступа (было 5).
    public int ReminderLeadDays { get; set; } = 7;

    public int BatchSize { get; set; } = 200;
}
