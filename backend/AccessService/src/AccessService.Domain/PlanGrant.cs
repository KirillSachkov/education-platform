using AccessService.Domain.Events;
using SharedKernel.DomainEvents;

namespace AccessService.Domain;

/// <summary>
/// Aggregate root: выданный пользователю grant на план доступа /
/// Aggregate root: a user's grant on a plan.
/// </summary>
public sealed class PlanGrant : AggregateRoot
{
    /// <summary>
    /// Maximum length of <see cref="RevokeReason"/>. Matches the DB column constraint
    /// (<c>plan_grants.revoke_reason VARCHAR(500)</c>). <see cref="Revoke"/> truncates
    /// reason values that exceed this, defending against attacker-supplied / accidental
    /// long strings producing a <c>DbUpdateException</c> at SaveChanges. Issue #252.
    /// </summary>
    public const int REVOKE_REASON_MAX_LENGTH = 500;

    /// <summary>
    /// Max length of <see cref="RebillId"/> / <see cref="CustomerKey"/> — payment-provider
    /// recurring identifiers (T-Bank RebillId / CustomerKey). Generous cap; matches DB column.
    /// </summary>
    public const int RECURRING_REF_MAX_LENGTH = 100;

    private PlanGrant() { } // EF

    private PlanGrant(
        Guid id,
        Guid userId,
        Guid planId,
        PlanGrantSource source,
        Guid? sourceRef,
        DateTimeOffset grantedAt,
        DateTimeOffset? expiresAt,
        long? pricePaidCents,
        long? upgradeBasePriceCents)
    {
        Id = id;
        UserId = userId;
        PlanId = planId;
        Source = source;
        SourceRef = sourceRef;
        GrantedAt = grantedAt;
        ExpiresAt = expiresAt;
        PricePaidCents = pricePaidCents;
        UpgradeBasePriceCents = upgradeBasePriceCents;
        Status = PlanGrantStatus.ACTIVE;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid PlanId { get; private set; }

    public PlanGrantSource Source { get; private set; }

    public Guid? SourceRef { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public PlanGrantStatus Status { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public string? RevokeReason { get; private set; }

    /// <summary>
    /// Сумма, которую юзер реально заплатил при выдаче (после credit-апгрейда). Снимок
    /// на момент выдачи — иммутабелен после <c>Create</c>. <c>null</c> для:
    /// <list type="bullet">
    ///   <item>FREE планов (бесплатные по определению)</item>
    ///   <item>ADMIN_GRANT (бесплатная выдача автором/админом)</item>
    ///   <item>AUTO_FREE / TRIAL (welcome-flow, не платится)</item>
    ///   <item>GITHUB_ORG / TELEGRAM_F1 (auto-issue, не платится)</item>
    ///   <item>MIGRATION (исторические grants до Phase 2 — backfill копирует
    ///     <c>plan.price_cents</c> только для PURCHASE/INVITE_LINK; admin-issue остаётся null)</item>
    ///   <item>INVITE_LINK (раздача автором — платность через invite TBD)</item>
    /// </list>
    /// Используется <c>UpgradeCreditCalculator</c> для расчёта скидки при апгрейде на
    /// более широкий план: credit = sum(price_paid_cents) для grants со scope ⊆ target.scope.
    /// Phase 2 / issue #112.
    /// </summary>
    public long? PricePaidCents { get; private set; }

    /// <summary>
    /// Снимок базовой цены целевого полного доступа на момент покупки временного paid trial.
    /// Если lifetime FULL_ALL подорожает позже, доплата считается от этой базы, а не от
    /// новой цены. null для обычных grants и legacy trial-грантов до появления правила.
    /// </summary>
    public long? UpgradeBasePriceCents { get; private set; }

    /// <summary>
    /// Legacy admin-оверрайд зачёта для trial-гранта (#580). Сейчас paid trial
    /// учитывается без срока сгорания; поле оставлено для совместимости админского endpoint'а.
    /// </summary>
    public DateTimeOffset? CreditOverrideUntil { get; private set; }

    /// <summary>
    /// Когда был отправлен reminder «пробник скоро истекает» (#580) — идемпотентность sweeper'а.
    /// null = ещё не отправляли.
    /// </summary>
    public DateTimeOffset? ExpiryReminderSentAt { get; private set; }

    /// <summary>
    /// Recurring-токен платёжного провайдера для безредиректного автосписания (T-Bank RebillId, #614).
    /// null = grant не подписочный (lifetime / разовый). Привязывается через <see cref="AttachRecurring"/>.
    /// </summary>
    public string? RebillId { get; private set; }

    /// <summary>
    /// Идентификатор покупателя у провайдера (T-Bank CustomerKey, #614) — нужен вместе с
    /// <see cref="RebillId"/> для server-side charge. null = grant не подписочный.
    /// </summary>
    public string? CustomerKey { get; private set; }

    /// <summary>
    /// Когда планируется следующее автосписание (#614). null = автопродление не настроено
    /// (lifetime grant либо подписка отменена). Двигается <see cref="Renew"/> /
    /// <see cref="RecordChargeFailure"/>.
    /// </summary>
    public DateTimeOffset? NextChargeAt { get; private set; }

    /// <summary>
    /// Счётчик подряд идущих неудачных автосписаний (#614). Сбрасывается в 0 на успешном
    /// <see cref="Renew"/>; инкрементируется в <see cref="RecordChargeFailure"/>. Дозревает до
    /// порога отмены подписки в A2 (recurring charge worker).
    /// </summary>
    public int ChargeFailureCount { get; private set; }

    /// <summary>
    /// Temporary access boundary while an automatic renewal is in dunning. The paid-through
    /// boundary remains <see cref="ExpiresAt"/>; this value must never be used for voluntary
    /// cancellation or a refunded renewal.
    /// </summary>
    public DateTimeOffset? RenewalGraceEndsAt { get; private set; }

    /// <summary>
    /// When the user (or a refund safeguard) disabled future automatic charges. Recurring
    /// provider references are retained so the user can explicitly resume before the paid
    /// period ends without another checkout.
    /// </summary>
    public DateTimeOffset? AutoRenewalCancelledAt { get; private set; }

    /// <summary>
    /// Effective access boundary. An enabled recurring grant always has a hard operational
    /// grace cutoff, even if the billing worker missed the first attempt and no failure row was
    /// materialized yet. Voluntary cancellation and refunds deliberately fall back to paid-through.
    /// </summary>
    public DateTimeOffset? AccessEndsAt =>
        ExpiresAt is { } paidThrough
        && RebillId is not null
        && CustomerKey is not null
        && AutoRenewalCancelledAt is null
            ? RenewalGraceEndsAt ?? SubscriptionRenewalPolicy.GraceEndsAt(paidThrough)
            : ExpiresAt;

    /// <summary>
    /// Создаёт новый ACTIVE grant. Поднимает <see cref="PlanGrantCreatedDomainEvent"/> /
    /// Creates a new ACTIVE grant. Raises <see cref="PlanGrantCreatedDomainEvent"/>.
    /// </summary>
    public static PlanGrant Create(
        Guid userId,
        Guid planId,
        PlanGrantSource source,
        Guid? sourceRef,
        DateTimeOffset? expiresAt = null,
        long? pricePaidCents = null,
        long? upgradeBasePriceCents = null)
    {
        PlanGrant grant = new(
            Guid.CreateVersion7(),
            userId,
            planId,
            source,
            sourceRef,
            DateTimeOffset.UtcNow,
            expiresAt,
            pricePaidCents,
            upgradeBasePriceCents);

        grant.RaiseDomainEvent(new PlanGrantCreatedDomainEvent(
            grant.Id,
            userId,
            planId,
            source,
            sourceRef));

        return grant;
    }

    /// <summary>
    /// Отзывает grant. Возвращает <c>grant.not.active</c> если grant уже не ACTIVE /
    /// Revokes the grant. Returns <c>grant.not.active</c> if status is not ACTIVE.
    /// </summary>
    public UnitResult<Error> Revoke(Guid revokedBy, string? reason)
    {
        if (Status != PlanGrantStatus.ACTIVE)
        {
            return AccessErrors.GrantNotActive();
        }

        Status = PlanGrantStatus.REVOKED;
        RevokedAt = DateTimeOffset.UtcNow;
        RevokedBy = revokedBy;
        // Truncate defensively — DB column caps reason at REVOKE_REASON_MAX_LENGTH and
        // unbounded user input could trigger a DbUpdateException at SaveChanges (#252 SEC-2).
        RevokeReason = reason is { Length: > REVOKE_REASON_MAX_LENGTH }
            ? reason[..REVOKE_REASON_MAX_LENGTH]
            : reason;

        RaiseDomainEvent(new PlanGrantRevokedDomainEvent(Id, UserId, PlanId, RevokeReason));

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Переводит grant в EXPIRED по истечении TTL. Идемпотентно для не-ACTIVE
    /// статусов / Transitions grant to EXPIRED on TTL elapsed. Idempotent for
    /// non-ACTIVE statuses (revoked grants stay revoked).
    /// </summary>
    public UnitResult<Error> Expire()
    {
        if (Status != PlanGrantStatus.ACTIVE)
        {
            return AccessErrors.GrantNotActive();
        }

        Status = PlanGrantStatus.EXPIRED;
        RaiseDomainEvent(new PlanGrantExpiredDomainEvent(Id, UserId, PlanId));
        return UnitResult.Success<Error>();
    }

    /// <summary>Админ-оверрайд (#580): продлевает действие зачёта пробника до <paramref name="until"/>.</summary>
    public void SetCreditOverride(DateTimeOffset until) => CreditOverrideUntil = until;

    /// <summary>Помечает, что reminder об истечении пробника отправлен (#580).</summary>
    public void MarkReminderSent(DateTimeOffset at) => ExpiryReminderSentAt = at;

    /// <summary>
    /// Привязывает recurring-токены провайдера к ACTIVE-grant'у (#614) после успешного
    /// первого списания — это переводит grant в режим автопродления. Идемпотентно для тех же
    /// значений; конфликт <c>grant.recurring.ref_conflict</c> при попытке перепривязать другой
    /// <paramref name="rebillId"/>. Только для ACTIVE (иначе <c>grant.not.active</c>).
    /// </summary>
    public UnitResult<Error> AttachRecurring(string rebillId, string customerKey, DateTimeOffset nextChargeAt)
    {
        if (Status != PlanGrantStatus.ACTIVE)
        {
            return AccessErrors.GrantNotActive();
        }

        if (string.IsNullOrWhiteSpace(rebillId) || string.IsNullOrWhiteSpace(customerKey))
        {
            return AccessErrors.RecurringRefRequired();
        }

        if (rebillId.Length > RECURRING_REF_MAX_LENGTH || customerKey.Length > RECURRING_REF_MAX_LENGTH)
        {
            return AccessErrors.RecurringRefTooLong();
        }

        if (RebillId is not null && !string.Equals(RebillId, rebillId, StringComparison.Ordinal))
        {
            return AccessErrors.RecurringRefConflict();
        }

        RebillId = rebillId;
        CustomerKey = customerKey;
        NextChargeAt = AutoRenewalCancelledAt is null ? nextChargeAt : null;
        ChargeFailureCount = 0;
        RenewalGraceEndsAt = null;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Продлевает подписку после успешного автосписания (#614): сдвигает <see cref="ExpiresAt"/>
    /// на новый срок, переносит <see cref="NextChargeAt"/> и сбрасывает счётчик ошибок.
    /// Разрешён для ACTIVE и EXPIRED: EXPIRED может возникнуть в гонке между
    /// ExpiredGrantsSweeper и уже подтверждённым банком renewal-платежом. В этом случае
    /// успешное списание обязано реактивировать доступ. REVOKED не восстанавливается.
    /// </summary>
    public UnitResult<Error> Renew(DateTimeOffset newExpiresAt, DateTimeOffset newNextChargeAt)
    {
        if (Status is not (PlanGrantStatus.ACTIVE or PlanGrantStatus.EXPIRED))
        {
            return AccessErrors.GrantNotActive();
        }

        Status = PlanGrantStatus.ACTIVE;
        ExpiresAt = newExpiresAt;
        NextChargeAt = AutoRenewalCancelledAt is null ? newNextChargeAt : null;
        ChargeFailureCount = 0;
        RenewalGraceEndsAt = null;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Removes exactly one paid renewal period from the current expiry. Later renewal
    /// periods are preserved because the duration is subtracted from the current value.
    /// A full refund disables auto-renew to prevent an immediate replacement charge.
    /// </summary>
    public Result<DateTimeOffset, Error> RollbackRenewal(
        TimeSpan paidPeriod,
        DateTimeOffset now)
    {
        if (Status is not (PlanGrantStatus.ACTIVE or PlanGrantStatus.EXPIRED))
            return AccessErrors.GrantNotActive();
        if (ExpiresAt is null || paidPeriod <= TimeSpan.Zero)
            return Error.Validation("grant.renewal_rollback.invalid", "Некорректный renewal-период для отката");

        DateTimeOffset rolledBackExpiresAt = ExpiresAt.Value.Subtract(paidPeriod);
        ExpiresAt = rolledBackExpiresAt;
        NextChargeAt = null;
        ChargeFailureCount = 0;
        RenewalGraceEndsAt = null;
        AutoRenewalCancelledAt ??= now;
        Status = rolledBackExpiresAt <= now
            ? PlanGrantStatus.EXPIRED
            : PlanGrantStatus.ACTIVE;
        return rolledBackExpiresAt;
    }

    /// <summary>Stops future charges when a renewal was refunded before local PAID apply.</summary>
    public UnitResult<Error> StopAutoRenewal(DateTimeOffset now)
    {
        if (Status is not (PlanGrantStatus.ACTIVE or PlanGrantStatus.EXPIRED))
            return AccessErrors.GrantNotActive();

        NextChargeAt = null;
        ChargeFailureCount = 0;
        RenewalGraceEndsAt = null;
        AutoRenewalCancelledAt ??= now;
        if (ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            Status = PlanGrantStatus.EXPIRED;
        }

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Фиксирует неудачное автосписание (#614): инкрементирует счётчик подряд идущих ошибок
    /// и планирует retry на <paramref name="nextRetryAt"/>. Только для ACTIVE
    /// (иначе <c>grant.not.active</c>). Решение об отмене подписки по достижении порога — A2.
    /// </summary>
    public UnitResult<Error> RecordChargeFailure(
        DateTimeOffset? nextRetryAt,
        DateTimeOffset? renewalGraceEndsAt = null)
    {
        if (Status != PlanGrantStatus.ACTIVE)
        {
            return AccessErrors.GrantNotActive();
        }

        if (AutoRenewalCancelledAt is not null)
        {
            return AccessErrors.AutoRenewalCancelled();
        }

        if (renewalGraceEndsAt is not null
            && (ExpiresAt is null || renewalGraceEndsAt < ExpiresAt))
        {
            return AccessErrors.RenewalGraceInvalid();
        }

        DateTimeOffset? effectiveGraceEndsAt = RenewalGraceEndsAt ?? renewalGraceEndsAt;
        if (nextRetryAt is not null
            && effectiveGraceEndsAt is not null
            && nextRetryAt > effectiveGraceEndsAt)
        {
            return AccessErrors.RenewalRetryAfterGrace();
        }

        // The first failed attempt fixes the grace boundary. A later retry must not
        // silently extend access because of a changed configuration or clock drift.
        RenewalGraceEndsAt ??= renewalGraceEndsAt;
        ChargeFailureCount++;
        NextChargeAt = nextRetryAt;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Disables future charges while preserving only the paid period. Idempotent for an
    /// already-cancelled recurring grant.
    /// </summary>
    public UnitResult<Error> CancelAutoRenewal(DateTimeOffset now)
    {
        if (AutoRenewalCancelledAt is not null)
        {
            return UnitResult.Success<Error>();
        }

        if (Status != PlanGrantStatus.ACTIVE)
        {
            return AccessErrors.GrantNotActive();
        }

        if (RebillId is null || CustomerKey is null || ExpiresAt is null)
        {
            return AccessErrors.RecurringNotConfigured();
        }

        AutoRenewalCancelledAt ??= now;
        NextChargeAt = null;
        ChargeFailureCount = 0;
        RenewalGraceEndsAt = null;

        if (ExpiresAt <= now)
        {
            Status = PlanGrantStatus.EXPIRED;
        }

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Explicitly re-enables a cancelled renewal before the paid period ends, or performs
    /// one user-initiated retry during terminal dunning grace. If the regular lead time has
    /// already elapsed, the next attempt is due immediately.
    /// </summary>
    public UnitResult<Error> ResumeAutoRenewal(DateTimeOffset now, TimeSpan chargeLeadTime)
    {
        if (Status != PlanGrantStatus.ACTIVE)
        {
            return AccessErrors.GrantNotActive();
        }

        if (RebillId is null || CustomerKey is null || ExpiresAt is null)
        {
            return AccessErrors.RecurringNotConfigured();
        }

        // A terminal dunning cycle has no scheduled retry, but the user may explicitly
        // restart it while the already-established grace period is still active. Preserve
        // that boundary: resuming must never extend access after failed charges.
        if (AutoRenewalCancelledAt is null)
        {
            bool isTerminalDunning = NextChargeAt is null
                && RenewalGraceEndsAt is not null
                && ChargeFailureCount >= SubscriptionRenewalPolicy.MAX_ATTEMPTS;
            if (!isTerminalDunning)
            {
                // Idempotent resume must not reset an ordinary active dunning cycle.
                return UnitResult.Success<Error>();
            }

            if (RenewalGraceEndsAt <= now)
            {
                return AccessErrors.RenewalPeriodEnded();
            }

            // This is one user-initiated payment retry, not a new automatic three-attempt
            // campaign. Keep it as the final budget slot: success resets the cycle in Renew;
            // failure returns to terminal dunning without rapid catch-up charges for T/T+48h.
            ChargeFailureCount = SubscriptionRenewalPolicy.MAX_ATTEMPTS - 1;
            NextChargeAt = now;
            return UnitResult.Success<Error>();
        }

        if (ExpiresAt <= now)
        {
            return AccessErrors.RenewalPeriodEnded();
        }

        if (chargeLeadTime <= TimeSpan.Zero)
        {
            return AccessErrors.RenewalLeadTimeInvalid();
        }

        AutoRenewalCancelledAt = null;
        RenewalGraceEndsAt = null;
        ChargeFailureCount = 0;
        DateTimeOffset scheduledAt = ExpiresAt.Value.Subtract(chargeLeadTime);
        NextChargeAt = scheduledAt > now ? scheduledAt : now;
        return UnitResult.Success<Error>();
    }
}
