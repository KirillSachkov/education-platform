using AccessService.Core.Database;
using AccessService.Domain;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.Billing.UseCases;

/// <summary>
/// Applies one definitive renewal failure through the single canonical dunning state machine.
/// Persistence remains the caller's responsibility so webhook audit, grant state and outbox
/// can commit in the caller's transaction.
/// </summary>
public sealed class RenewalFailureRecorder
{
    private readonly IOutboxService _outbox;

    public RenewalFailureRecorder(IOutboxService outbox) => _outbox = outbox;

    public async Task<Result<RenewalFailureTransition, Error>> RecordAsync(
        PlanGrant grant,
        Plan plan,
        Order order,
        string? reason,
        DateTimeOffset failedAt)
    {
        UnitResult<Error> markFailed = order.MarkFailed(reason);
        if (markFailed.IsFailure)
        {
            return markFailed.Error;
        }

        if (grant.ExpiresAt is not { } paidThrough)
        {
            return Error.Validation(
                "grant.renewal.paid_through_missing",
                "Recurring grant must have ExpiresAt");
        }

        int completedFailureCount = grant.ChargeFailureCount + 1;
        DateTimeOffset? nextRetryAt = SubscriptionRenewalPolicy.NextRetryAt(
            paidThrough,
            completedFailureCount);
        DateTimeOffset graceEndsAt = SubscriptionRenewalPolicy.GraceEndsAt(paidThrough);
        UnitResult<Error> recorded = grant.RecordChargeFailure(nextRetryAt, graceEndsAt);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        bool terminal = grant.ChargeFailureCount >= SubscriptionRenewalPolicy.MAX_ATTEMPTS
            || nextRetryAt is null;
        string stage = terminal
            ? SubscriptionLifecycleStages.TerminalFailure
            : SubscriptionLifecycleStages.RetryScheduled;

        await _outbox.PublishAsync(new PlanGrantRenewalFailed(
            grant.Id,
            grant.UserId,
            grant.PlanId,
            plan.Tier.ToString(),
            plan.AuthorId,
            grant.ExpiresAt,
            grant.ChargeFailureCount,
            reason,
            order.Id,
            failedAt,
            nextRetryAt,
            graceEndsAt,
            stage));

        return new RenewalFailureTransition(terminal, nextRetryAt, graceEndsAt);
    }
}

public sealed record RenewalFailureTransition(
    bool IsTerminal,
    DateTimeOffset? NextRetryAt,
    DateTimeOffset GraceEndsAt);
