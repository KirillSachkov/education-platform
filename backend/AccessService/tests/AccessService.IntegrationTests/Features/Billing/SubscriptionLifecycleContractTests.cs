using System.Text.Json;
using Shared.Messaging.IntegrationEvents.Access;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.IntegrationTests.Features.Billing;

public sealed class SubscriptionLifecycleContractTests
{
    [Fact]
    public void RenewalSuccess_CarriesLifecycleMetadata_AndCorrelatesByOrder()
    {
        Guid grantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTimeOffset renewedAt = DateTimeOffset.UtcNow;
        DateTimeOffset previousExpiresAt = renewedAt.AddDays(2);
        DateTimeOffset expiresAt = renewedAt.AddDays(30);
        DateTimeOffset nextChargeAt = expiresAt.AddDays(-1);
        DateTimeOffset graceEndsAt = expiresAt.AddDays(3);

        PlanGrantRenewed message = new(
            grantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SUBSCRIPTION",
            Guid.NewGuid(),
            expiresAt,
            orderId,
            renewedAt,
            previousExpiresAt,
            nextChargeAt,
            graceEndsAt,
            Attempt: 2,
            Stage: SubscriptionLifecycleStages.Renewed);

        PlanGrantRenewed roundTrip = RoundTrip(message);

        Assert.Equal(orderId, roundTrip.RenewalOrderId);
        Assert.Equal(renewedAt, roundTrip.RenewedAt);
        Assert.Equal(previousExpiresAt, roundTrip.PreviousExpiresAt);
        Assert.Equal(nextChargeAt, roundTrip.NextChargeAt);
        Assert.Equal(graceEndsAt, roundTrip.GraceEndsAt);
        Assert.Equal(2, roundTrip.Attempt);
        Assert.Equal(SubscriptionLifecycleStages.Renewed, roundTrip.Stage);
        Assert.Equal(orderId, roundTrip.CorrelationId);
    }

    [Fact]
    public void RenewalFailure_CarriesRetryWindow_AndCorrelatesByOrder()
    {
        Guid grantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;
        DateTimeOffset nextRetryAt = failedAt.AddDays(1);
        DateTimeOffset graceEndsAt = failedAt.AddDays(3);

        PlanGrantRenewalFailed message = new(
            grantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SUBSCRIPTION",
            Guid.NewGuid(),
            failedAt.AddDays(1),
            FailureCount: 1,
            FailureReason: "charge.declined",
            orderId,
            failedAt,
            nextRetryAt,
            graceEndsAt,
            SubscriptionLifecycleStages.RetryScheduled);

        PlanGrantRenewalFailed roundTrip = RoundTrip(message);

        Assert.Equal(orderId, roundTrip.RenewalOrderId);
        Assert.Equal(failedAt, roundTrip.FailedAt);
        Assert.Equal(nextRetryAt, roundTrip.NextRetryAt);
        Assert.Equal(graceEndsAt, roundTrip.GraceEndsAt);
        Assert.Equal(1, roundTrip.Attempt);
        Assert.Equal(SubscriptionLifecycleStages.RetryScheduled, roundTrip.Stage);
        Assert.Equal(orderId, roundTrip.CorrelationId);
    }

    [Fact]
    public void RenewalCancellation_CarriesPaidThroughDate_AndExplicitCorrelation()
    {
        Guid correlationId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTimeOffset cancelledAt = DateTimeOffset.UtcNow;
        DateTimeOffset accessEndsAt = cancelledAt.AddDays(20);

        PlanGrantRenewalCancelled message = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SUBSCRIPTION",
            Guid.NewGuid(),
            orderId,
            cancelledAt,
            accessEndsAt,
            Attempt: 1,
            correlationId);

        PlanGrantRenewalCancelled roundTrip = RoundTrip(message);

        Assert.Equal(orderId, roundTrip.RenewalOrderId);
        Assert.Equal(cancelledAt, roundTrip.CancelledAt);
        Assert.Equal(accessEndsAt, roundTrip.AccessEndsAt);
        Assert.Equal(1, roundTrip.Attempt);
        Assert.Equal(SubscriptionLifecycleStages.Cancelled, roundTrip.Stage);
        Assert.Equal(correlationId, roundTrip.CorrelationId);
    }

    [Fact]
    public void RenewalResume_CarriesNextChargeAndGraceDates_AndExplicitCorrelation()
    {
        Guid correlationId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTimeOffset resumedAt = DateTimeOffset.UtcNow;
        DateTimeOffset nextChargeAt = resumedAt.AddHours(8);
        DateTimeOffset graceEndsAt = resumedAt.AddDays(4);

        PlanGrantRenewalResumed message = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SUBSCRIPTION",
            Guid.NewGuid(),
            orderId,
            resumedAt,
            nextChargeAt,
            graceEndsAt,
            Attempt: 0,
            correlationId);

        PlanGrantRenewalResumed roundTrip = RoundTrip(message);

        Assert.Equal(orderId, roundTrip.RenewalOrderId);
        Assert.Equal(resumedAt, roundTrip.ResumedAt);
        Assert.Equal(nextChargeAt, roundTrip.NextChargeAt);
        Assert.Equal(graceEndsAt, roundTrip.GraceEndsAt);
        Assert.Equal(0, roundTrip.Attempt);
        Assert.Equal(SubscriptionLifecycleStages.Resumed, roundTrip.Stage);
        Assert.Equal(correlationId, roundTrip.CorrelationId);
    }

    [Fact]
    public void RenewalLifecycle_RoutingKeys_AreStable()
    {
        Assert.Equal("plan_grant.renewed", AccessEventsRouting.RoutingKeys.PlanGrantRenewed());
        Assert.Equal("plan_grant.renewal_failed", AccessEventsRouting.RoutingKeys.PlanGrantRenewalFailed());
        Assert.Equal("plan_grant.renewal_cancelled", AccessEventsRouting.RoutingKeys.PlanGrantRenewalCancelled());
        Assert.Equal("plan_grant.renewal_resumed", AccessEventsRouting.RoutingKeys.PlanGrantRenewalResumed());
    }

    [Fact]
    public void ExistingRenewalConstructors_RemainSourceCompatible()
    {
        Guid renewedGrantId = Guid.NewGuid();
        Guid failedGrantId = Guid.NewGuid();

        PlanGrantRenewed renewed = new(
            renewedGrantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SUBSCRIPTION",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        PlanGrantRenewalFailed failed = new(
            failedGrantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SUBSCRIPTION",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            3,
            "legacy");

        Assert.Equal(renewedGrantId, renewed.CorrelationId);
        Assert.Equal(SubscriptionLifecycleStages.Renewed, renewed.Stage);
        Assert.Equal(failedGrantId, failed.CorrelationId);
        Assert.Equal(SubscriptionLifecycleStages.TerminalFailure, failed.Stage);
    }

    private static T RoundTrip<T>(T message)
    {
        string json = JsonSerializer.Serialize(message);
        return JsonSerializer.Deserialize<T>(json)!;
    }
}
