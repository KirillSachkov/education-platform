using AccessService.Domain;
using AccessService.Domain.Events;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

/// <summary>
/// Pure domain tests for <see cref="PlanGrant"/>. No DB / HTTP — only factory +
/// revoke rules + domain-event raising. Persistence is covered later (Phase D+E).
/// </summary>
public class PlanGrantFactoryTests
{
    [Fact]
    public void SubscriptionRenewalPolicy_has_three_attempts_and_fixed_grace()
    {
        DateTimeOffset paidThrough = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(paidThrough.AddHours(-24), SubscriptionRenewalPolicy.FirstChargeAt(paidThrough));
        Assert.Equal(paidThrough, SubscriptionRenewalPolicy.NextRetryAt(paidThrough, 1));
        Assert.Equal(paidThrough.AddHours(48), SubscriptionRenewalPolicy.NextRetryAt(paidThrough, 2));
        Assert.Null(SubscriptionRenewalPolicy.NextRetryAt(paidThrough, 3));
        Assert.Equal(paidThrough.AddHours(72), SubscriptionRenewalPolicy.GraceEndsAt(paidThrough));
        Assert.Equal(3, SubscriptionRenewalPolicy.MAX_ATTEMPTS);
    }

    [Fact]
    public void Create_returns_active_grant_and_raises_created_event()
    {
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        Guid sourceRef = Guid.NewGuid();

        PlanGrant grant = PlanGrant.Create(
            userId,
            planId,
            PlanGrantSource.INVITE_LINK,
            sourceRef);

        Assert.Equal(PlanGrantStatus.ACTIVE, grant.Status);
        Assert.Equal(userId, grant.UserId);
        Assert.Equal(planId, grant.PlanId);
        Assert.Equal(PlanGrantSource.INVITE_LINK, grant.Source);
        Assert.Equal(sourceRef, grant.SourceRef);
        Assert.Null(grant.RevokedAt);
        Assert.Null(grant.RevokedBy);
        Assert.Null(grant.RevokeReason);

        Assert.Single(grant.DomainEvents);
        PlanGrantCreatedDomainEvent created = Assert.IsType<PlanGrantCreatedDomainEvent>(grant.DomainEvents[0]);
        Assert.Equal(grant.Id, created.GrantId);
        Assert.Equal(userId, created.UserId);
        Assert.Equal(planId, created.PlanId);
        Assert.Equal(PlanGrantSource.INVITE_LINK, created.Source);
        Assert.Equal(sourceRef, created.SourceRef);
    }

    [Fact]
    public void Revoke_active_grant_succeeds_and_raises_revoked_event()
    {
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        Guid revokedBy = Guid.NewGuid();

        PlanGrant grant = PlanGrant.Create(
            userId,
            planId,
            PlanGrantSource.ADMIN_GRANT,
            sourceRef: null);
        grant.ClearDomainEvents(); // discard PlanGrantCreatedDomainEvent

        UnitResult<Error> result = grant.Revoke(revokedBy, "abuse");

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanGrantStatus.REVOKED, grant.Status);
        Assert.NotNull(grant.RevokedAt);
        Assert.Equal(revokedBy, grant.RevokedBy);
        Assert.Equal("abuse", grant.RevokeReason);

        Assert.Single(grant.DomainEvents);
        PlanGrantRevokedDomainEvent revoked = Assert.IsType<PlanGrantRevokedDomainEvent>(grant.DomainEvents[0]);
        Assert.Equal(grant.Id, revoked.GrantId);
        Assert.Equal(userId, revoked.UserId);
        Assert.Equal(planId, revoked.PlanId);
        Assert.Equal("abuse", revoked.Reason);
    }

    [Fact]
    public void Revoke_already_revoked_grant_fails_with_grant_not_active()
    {
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PlanGrantSource.MIGRATION,
            sourceRef: null);

        UnitResult<Error> first = grant.Revoke(Guid.NewGuid(), reason: null);
        Assert.True(first.IsSuccess);

        UnitResult<Error> second = grant.Revoke(Guid.NewGuid(), reason: "second try");

        Assert.True(second.IsFailure);
        Assert.Equal("grant.not.active", second.Error.Messages[0].Code);
    }

    [Fact]
    public void Expire_active_grant_succeeds_and_raises_expired_event()
    {
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PlanGrantSource.TRIAL,
            sourceRef: null,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        grant.ClearDomainEvents();

        UnitResult<Error> result = grant.Expire();

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanGrantStatus.EXPIRED, grant.Status);
        Assert.Single(grant.DomainEvents);
        PlanGrantExpiredDomainEvent expired = Assert.IsType<PlanGrantExpiredDomainEvent>(grant.DomainEvents[0]);
        Assert.Equal(grant.Id, expired.GrantId);
    }

    [Fact]
    public void Expire_already_expired_grant_fails()
    {
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.TRIAL, sourceRef: null);

        UnitResult<Error> first = grant.Expire();
        Assert.True(first.IsSuccess);

        UnitResult<Error> second = grant.Expire();
        Assert.True(second.IsFailure);
        Assert.Equal("grant.not.active", second.Error.Messages[0].Code);
    }

    [Fact]
    public void Expire_revoked_grant_fails()
    {
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.TRIAL, sourceRef: null);
        grant.Revoke(Guid.NewGuid(), reason: "test");

        UnitResult<Error> result = grant.Expire();

        Assert.True(result.IsFailure);
        Assert.Equal("grant.not.active", result.Error.Messages[0].Code);
    }

    // ---- Recurring auto-renew subscription methods (#614) ----

    private static PlanGrant ActiveGrant() =>
        PlanGrant.Create(Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.PURCHASE, sourceRef: null);

    [Fact]
    public void AttachRecurring_on_active_grant_sets_fields_and_resets_failures()
    {
        PlanGrant grant = ActiveGrant();
        DateTimeOffset nextCharge = DateTimeOffset.UtcNow.AddDays(30);

        UnitResult<Error> result = grant.AttachRecurring("rb-1", "ck-1", nextCharge);

        Assert.True(result.IsSuccess);
        Assert.Equal("rb-1", grant.RebillId);
        Assert.Equal("ck-1", grant.CustomerKey);
        Assert.Equal(nextCharge, grant.NextChargeAt);
        Assert.Equal(0, grant.ChargeFailureCount);
    }

    [Fact]
    public void AttachRecurring_with_empty_refs_fails()
    {
        PlanGrant grant = ActiveGrant();

        UnitResult<Error> result = grant.AttachRecurring("", "ck-1", DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("grant.recurring.ref_required", result.Error.Messages[0].Code);
    }

    [Fact]
    public void AttachRecurring_same_rebill_id_is_idempotent()
    {
        PlanGrant grant = ActiveGrant();
        DateTimeOffset next = DateTimeOffset.UtcNow.AddDays(30);
        Assert.True(grant.AttachRecurring("rb-1", "ck-1", next).IsSuccess);

        UnitResult<Error> again = grant.AttachRecurring("rb-1", "ck-2", next.AddDays(1));

        Assert.True(again.IsSuccess);
        Assert.Equal("rb-1", grant.RebillId);
    }

    [Fact]
    public void AttachRecurring_with_conflicting_rebill_id_fails()
    {
        PlanGrant grant = ActiveGrant();
        Assert.True(grant.AttachRecurring("rb-1", "ck-1", DateTimeOffset.UtcNow.AddDays(30)).IsSuccess);

        UnitResult<Error> conflict = grant.AttachRecurring("rb-2", "ck-1", DateTimeOffset.UtcNow.AddDays(30));

        Assert.True(conflict.IsFailure);
        Assert.Equal("grant.recurring.ref_conflict", conflict.Error.Messages[0].Code);
    }

    [Fact]
    public void AttachRecurring_on_revoked_grant_fails()
    {
        PlanGrant grant = ActiveGrant();
        grant.Revoke(Guid.NewGuid(), reason: "test");

        UnitResult<Error> result = grant.AttachRecurring("rb-1", "ck-1", DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("grant.not.active", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Renew_extends_expiry_and_resets_failure_count()
    {
        PlanGrant grant = ActiveGrant();
        grant.AttachRecurring("rb-1", "ck-1", DateTimeOffset.UtcNow.AddDays(30));
        grant.RecordChargeFailure(DateTimeOffset.UtcNow.AddDays(1)); // bump failures to 1

        DateTimeOffset newExpires = DateTimeOffset.UtcNow.AddDays(30);
        DateTimeOffset newNextCharge = DateTimeOffset.UtcNow.AddDays(30);
        UnitResult<Error> result = grant.Renew(newExpires, newNextCharge);

        Assert.True(result.IsSuccess);
        Assert.Equal(newExpires, grant.ExpiresAt);
        Assert.Equal(newNextCharge, grant.NextChargeAt);
        Assert.Equal(0, grant.ChargeFailureCount);
    }

    [Fact]
    public void Renew_on_expired_grant_reactivates_after_confirmed_charge()
    {
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.PURCHASE,
            sourceRef: null, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        grant.Expire();

        DateTimeOffset newExpires = DateTimeOffset.UtcNow.AddDays(30);
        UnitResult<Error> result = grant.Renew(newExpires, newExpires.AddHours(-1));

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanGrantStatus.ACTIVE, grant.Status);
        Assert.Equal(newExpires, grant.ExpiresAt);
    }

    [Fact]
    public void RollbackRenewal_SubtractsOnlyRefundedPeriod_AndPreservesLaterRenewals()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset currentExpiresAt = now.AddDays(90);
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(),
            expiresAt: currentExpiresAt);
        Assert.True(grant.AttachRecurring("rb-refund", "customer", now.AddDays(89)).IsSuccess);
        Assert.True(grant.RecordChargeFailure(now.AddDays(1), now.AddDays(93)).IsSuccess);

        Result<DateTimeOffset, Error> result = grant.RollbackRenewal(TimeSpan.FromDays(30), now);

        Assert.True(result.IsSuccess);
        Assert.Equal(currentExpiresAt.AddDays(-30), result.Value);
        Assert.Equal(currentExpiresAt.AddDays(-30), grant.ExpiresAt);
        Assert.Equal(PlanGrantStatus.ACTIVE, grant.Status);
        Assert.Null(grant.NextChargeAt);
        Assert.Null(grant.RenewalGraceEndsAt);
        Assert.Equal(now, grant.AutoRenewalCancelledAt);
    }

    [Fact]
    public void RecordChargeFailure_increments_count_and_sets_next_retry()
    {
        PlanGrant grant = ActiveGrant();
        DateTimeOffset retry1 = DateTimeOffset.UtcNow.AddDays(1);
        DateTimeOffset retry2 = DateTimeOffset.UtcNow.AddDays(3);

        Assert.True(grant.RecordChargeFailure(retry1).IsSuccess);
        Assert.Equal(1, grant.ChargeFailureCount);
        Assert.Equal(retry1, grant.NextChargeAt);

        Assert.True(grant.RecordChargeFailure(retry2).IsSuccess);
        Assert.Equal(2, grant.ChargeFailureCount);
        Assert.Equal(retry2, grant.NextChargeAt);
    }

    [Fact]
    public void RecordChargeFailure_on_revoked_grant_fails()
    {
        PlanGrant grant = ActiveGrant();
        grant.Revoke(Guid.NewGuid(), reason: "test");

        UnitResult<Error> result = grant.RecordChargeFailure(DateTimeOffset.UtcNow.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("grant.not.active", result.Error.Messages[0].Code);
    }

    [Fact]
    public void RecordChargeFailure_starts_grace_and_never_extends_it()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.AddDays(1);
        DateTimeOffset graceEndsAt = expiresAt.AddDays(3);
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(), expiresAt: expiresAt);
        Assert.True(grant.AttachRecurring("rb-grace", "customer", now).IsSuccess);

        Assert.True(grant.RecordChargeFailure(expiresAt, graceEndsAt).IsSuccess);
        Assert.True(grant.RecordChargeFailure(expiresAt.AddDays(2), graceEndsAt.AddDays(1)).IsSuccess);

        Assert.Equal(2, grant.ChargeFailureCount);
        Assert.Equal(expiresAt.AddDays(2), grant.NextChargeAt);
        Assert.Equal(graceEndsAt, grant.RenewalGraceEndsAt);
        Assert.Equal(graceEndsAt, grant.AccessEndsAt);
    }

    [Fact]
    public void RecordChargeFailure_rejects_retry_after_grace()
    {
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddDays(1);
        DateTimeOffset graceEndsAt = expiresAt.AddDays(3);
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(), expiresAt: expiresAt);
        Assert.True(grant.AttachRecurring("rb-grace", "customer", expiresAt.AddDays(-1)).IsSuccess);

        UnitResult<Error> result = grant.RecordChargeFailure(graceEndsAt.AddTicks(1), graceEndsAt);

        Assert.True(result.IsFailure);
        Assert.Equal("grant.renewal.retry_after_grace", result.Error.Messages[0].Code);
        Assert.Null(grant.RenewalGraceEndsAt);
        Assert.Equal(0, grant.ChargeFailureCount);
    }

    [Fact]
    public void CancelAutoRenewal_is_idempotent_and_preserves_paid_period()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.AddDays(10);
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(), expiresAt: expiresAt);
        Assert.True(grant.AttachRecurring("rb-cancel", "customer", now.AddDays(9)).IsSuccess);
        Assert.True(grant.RecordChargeFailure(now.AddDays(1), expiresAt.AddDays(3)).IsSuccess);

        Assert.True(grant.CancelAutoRenewal(now).IsSuccess);
        Assert.True(grant.CancelAutoRenewal(now.AddMinutes(1)).IsSuccess);

        Assert.Equal(PlanGrantStatus.ACTIVE, grant.Status);
        Assert.Equal(expiresAt, grant.ExpiresAt);
        Assert.Equal(now, grant.AutoRenewalCancelledAt);
        Assert.Null(grant.NextChargeAt);
        Assert.Null(grant.RenewalGraceEndsAt);
        Assert.Equal(0, grant.ChargeFailureCount);
        Assert.Equal(expiresAt, grant.AccessEndsAt);
    }

    [Fact]
    public void ResumeAutoRenewal_schedules_safe_attempt_before_paid_expiry()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.AddHours(12);
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(), expiresAt: expiresAt);
        Assert.True(grant.AttachRecurring("rb-resume", "customer", now).IsSuccess);
        Assert.True(grant.CancelAutoRenewal(now).IsSuccess);

        UnitResult<Error> result = grant.ResumeAutoRenewal(now.AddMinutes(1), TimeSpan.FromHours(24));

        Assert.True(result.IsSuccess);
        Assert.Null(grant.AutoRenewalCancelledAt);
        Assert.Equal(now.AddMinutes(1), grant.NextChargeAt);
        Assert.Equal(0, grant.ChargeFailureCount);
    }

    [Fact]
    public void ResumeAutoRenewal_after_paid_period_fails()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(), expiresAt: now.AddMinutes(1));
        Assert.True(grant.AttachRecurring("rb-expired", "customer", now).IsSuccess);
        Assert.True(grant.CancelAutoRenewal(now).IsSuccess);

        UnitResult<Error> result = grant.ResumeAutoRenewal(now.AddMinutes(2), TimeSpan.FromHours(24));

        Assert.True(result.IsFailure);
        Assert.Equal("grant.renewal.period_ended", result.Error.Messages[0].Code);
    }

    [Fact]
    public void ResumeAutoRenewal_when_already_enabled_does_not_reset_dunning()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.AddDays(1);
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(), expiresAt: expiresAt);
        Assert.True(grant.AttachRecurring("rb-active", "customer", now).IsSuccess);
        Assert.True(grant.RecordChargeFailure(expiresAt, expiresAt.AddDays(3)).IsSuccess);

        Assert.True(grant.ResumeAutoRenewal(now, TimeSpan.FromHours(24)).IsSuccess);

        Assert.Equal(1, grant.ChargeFailureCount);
        Assert.Equal(expiresAt, grant.NextChargeAt);
        Assert.Equal(expiresAt.AddDays(3), grant.RenewalGraceEndsAt);
    }

    [Fact]
    public void ResumeAutoRenewal_terminal_dunning_schedules_immediate_attempt_and_preserves_grace()
    {
        DateTimeOffset expiresAt = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset graceEndsAt = SubscriptionRenewalPolicy.GraceEndsAt(expiresAt);
        DateTimeOffset now = expiresAt.AddHours(60);
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(), expiresAt: expiresAt);
        Assert.True(grant.AttachRecurring("rb-terminal", "customer", expiresAt.AddDays(-1)).IsSuccess);
        for (int completedAttempts = 1; completedAttempts <= SubscriptionRenewalPolicy.MAX_ATTEMPTS; completedAttempts++)
        {
            Assert.True(grant.RecordChargeFailure(
                SubscriptionRenewalPolicy.NextRetryAt(expiresAt, completedAttempts),
                graceEndsAt).IsSuccess);
        }

        UnitResult<Error> result = grant.ResumeAutoRenewal(now, SubscriptionRenewalPolicy.ChargeLeadTime);

        Assert.True(result.IsSuccess);
        Assert.Null(grant.AutoRenewalCancelledAt);
        Assert.Equal(SubscriptionRenewalPolicy.MAX_ATTEMPTS - 1, grant.ChargeFailureCount);
        Assert.Equal(now, grant.NextChargeAt);
        Assert.Equal(graceEndsAt, grant.RenewalGraceEndsAt);
        Assert.Equal(graceEndsAt, grant.AccessEndsAt);
    }

    [Fact]
    public void ResumeAutoRenewal_terminal_dunning_after_grace_fails_without_mutation()
    {
        DateTimeOffset expiresAt = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset graceEndsAt = SubscriptionRenewalPolicy.GraceEndsAt(expiresAt);
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(), expiresAt: expiresAt);
        Assert.True(grant.AttachRecurring("rb-terminal", "customer", expiresAt.AddDays(-1)).IsSuccess);
        for (int completedAttempts = 1; completedAttempts <= SubscriptionRenewalPolicy.MAX_ATTEMPTS; completedAttempts++)
        {
            Assert.True(grant.RecordChargeFailure(
                SubscriptionRenewalPolicy.NextRetryAt(expiresAt, completedAttempts),
                graceEndsAt).IsSuccess);
        }

        UnitResult<Error> result = grant.ResumeAutoRenewal(
            graceEndsAt.AddTicks(1),
            SubscriptionRenewalPolicy.ChargeLeadTime);

        Assert.True(result.IsFailure);
        Assert.Equal("grant.renewal.period_ended", result.Error.Messages[0].Code);
        Assert.Equal(SubscriptionRenewalPolicy.MAX_ATTEMPTS, grant.ChargeFailureCount);
        Assert.Null(grant.NextChargeAt);
        Assert.Equal(graceEndsAt, grant.RenewalGraceEndsAt);
    }
}
