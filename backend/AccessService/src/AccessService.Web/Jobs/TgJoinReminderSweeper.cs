using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.TgJoinReminders;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Contracts.HttpCommunication;

namespace AccessService.Web.Jobs;

/// <summary>
///     Periodically scans active <see cref="TgJoinReminder"/> rows (#616, ST-3) — those with
///     <c>CompletedAt IS NULL AND RemindersSent &lt; 2</c> — and nudges the user to join the
///     plan's Telegram community group:
///     <list type="bullet">
///       <item><c>RemindersSent == 0 &amp;&amp; age &gt;= FirstReminderAfter</c> → REMINDER_1;</item>
///       <item><c>RemindersSent == 1 &amp;&amp; age &gt;= SecondReminderAfter</c> → REMINDER_2;</item>
///       <item>otherwise → skip this cycle.</item>
///     </list>
///     Before publishing, a safety membership-check (<see cref="ITelegramBotServiceClient.CheckPlanMembershipAsync"/>)
///     guards against nudging someone who already joined out-of-band: member → <c>MarkCompleted</c>,
///     no nudge; unknown / TBS down → skip (don't burn a reminder); not-member → publish
///     <see cref="TgJoinReminderRequested"/> + <see cref="TgJoinReminder.MarkReminded"/>.
///
///     Mirrors <c>TrialExpiryReminderSweeper</c>. Hosted only in non-Testing environments;
///     integration tests instantiate it directly and call <see cref="SweepOnceAsync"/>.
/// </summary>
public sealed class TgJoinReminderSweeper : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<TgJoinReminderSweeperOptions> _options;
    private readonly ILogger<TgJoinReminderSweeper> _logger;

    public TgJoinReminderSweeper(
        IServiceProvider services,
        IOptionsMonitor<TgJoinReminderSweeperOptions> options,
        ILogger<TgJoinReminderSweeper> logger)
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
            await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken);
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
                _logger.LogError(ex, "TgJoinReminderSweeper iteration failed; will retry next tick");
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

    /// <summary>
    ///     Runs a single sweep pass. Returns the number of reminders actually published
    ///     (membership-skips / completions don't count). Public for direct invocation in tests.
    /// </summary>
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        TgJoinReminderSweeperOptions opts = _options.CurrentValue;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int batchSize = Math.Max(1, opts.BatchSize);

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        ITgJoinRemindersRepository reminders =
            scope.ServiceProvider.GetRequiredService<ITgJoinRemindersRepository>();
        ITelegramBotServiceClient telegram =
            scope.ServiceProvider.GetRequiredService<ITelegramBotServiceClient>();
        IOutboxService outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        ITransactionManager transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        IPlansRepository plans = scope.ServiceProvider.GetRequiredService<IPlansRepository>();

        IReadOnlyList<TgJoinReminder> batch = await reminders.GetDueBatchAsync(
            TgJoinReminderStaging.MAX_REMINDERS, batchSize, cancellationToken);

        if (batch.Count == 0)
        {
            return 0;
        }

        int reminded = 0;
        int completed = 0;

        foreach (TgJoinReminder reminder in batch)
        {
            string? stage = TgJoinReminderStaging.DueStage(
                reminder.RemindersSent,
                now - reminder.CreatedAt,
                opts.FirstReminderAfter,
                opts.SecondReminderAfter);

            if (stage is null)
            {
                continue; // not due yet by age.
            }

            // Safety membership-check — don't nudge someone who already joined out-of-band.
            Result<PlanMembershipDto, Error> membership = await telegram.CheckPlanMembershipAsync(
                reminder.UserId, reminder.PlanId, cancellationToken);

            if (membership.IsFailure)
            {
                _logger.LogDebug(
                    "TgJoin sweep skip (TBS unavailable): user={UserId} plan={PlanId}",
                    reminder.UserId, reminder.PlanId);
                continue; // don't burn a reminder on an unknown membership state.
            }

            if (membership.Value.IsMember)
            {
                reminder.MarkCompleted(now);
                completed++;
                continue;
            }

            // status == not_member (or unknown reported via IsMember=false + Status). Treat a
            // non-"member" Status as "uncertain" only when IsMember is false but the bot couldn't
            // resolve membership — be conservative and skip to avoid wrongly nudging a member.
            if (!string.Equals(membership.Value.Status, "not_member", StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "TgJoin sweep skip (membership status={Status}): user={UserId} plan={PlanId}",
                    membership.Value.Status, reminder.UserId, reminder.PlanId);
                continue;
            }

            // Resolve the plan's display name so REMINDER copy isn't the generic fallback «курс».
            // Best-effort: AccessService owns the plans table locally; if the plan is gone (rare
            // hard-delete) the consumer falls back to a generic noun.
            Result<Plan, Error> plan = await plans.GetByAsync(p => p.Id == reminder.PlanId, cancellationToken);
            string planName = plan.IsSuccess ? plan.Value.DisplayName.Value : string.Empty;

            await outbox.PublishAsync(new TgJoinReminderRequested(
                reminder.UserId,
                reminder.PlanId,
                reminder.GrantId,
                PlanName: planName,
                Stage: stage,
                OccurredAt: now));
            reminder.MarkReminded(now);
            reminded++;
        }

        UnitResult<Error> save = await transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            _logger.LogError(
                "TgJoinReminderSweeper failed to persist {Reminded} reminder(s) + {Completed} completion(s): {Error}",
                reminded, completed, save.Error.Type);
            return 0;
        }

        if (reminded > 0 || completed > 0)
        {
            _logger.LogInformation(
                "TgJoinReminderSweeper published {Reminded} reminder(s), auto-completed {Completed} (batch={Batch})",
                reminded, completed, batch.Count);
        }

        return reminded;
    }
}

/// <summary>
///     Pure due-staging logic for <see cref="TgJoinReminderSweeper"/> — extracted so it can be
///     unit-tested without a host / DB. Decides which nudge stage (if any) is due given how many
///     reminders were already sent and the row's age.
/// </summary>
public static class TgJoinReminderStaging
{
    /// <summary>Max reminders (REMINDER_1 + REMINDER_2) before we stop nudging.</summary>
    public const int MAX_REMINDERS = 2;

    /// <summary>
    ///     Returns the due stage (<see cref="TgJoinReminderStages.Reminder1"/> /
    ///     <see cref="TgJoinReminderStages.Reminder2"/>) or <c>null</c> if nothing is due yet.
    /// </summary>
    public static string? DueStage(
        int remindersSent,
        TimeSpan age,
        TimeSpan firstReminderAfter,
        TimeSpan secondReminderAfter) =>
        remindersSent switch
        {
            0 when age >= firstReminderAfter => TgJoinReminderStages.Reminder1,
            1 when age >= secondReminderAfter => TgJoinReminderStages.Reminder2,
            _ => null,
        };
}

public sealed class TgJoinReminderSweeperOptions
{
    public const string SectionName = "TgJoinReminderSweeper";

    /// <summary>How often the sweeper wakes up. Default 1h.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Row age before REMINDER_1 is due. Default 2 days.</summary>
    public TimeSpan FirstReminderAfter { get; set; } = TimeSpan.FromDays(2);

    /// <summary>Row age before REMINDER_2 is due. Default 9 days (2 + 7).</summary>
    public TimeSpan SecondReminderAfter { get; set; } = TimeSpan.FromDays(9);

    /// <summary>Max rows processed per sweep pass.</summary>
    public int BatchSize { get; set; } = 200;
}
