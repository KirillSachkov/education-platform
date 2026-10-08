using System.Text.Json;
using AccessService.Contracts.Billing;
using AccessService.Core.Database;
using AccessService.Core.Features.Billing.Diagnostics;
using AccessService.Core.Features.Plans;
using AccessService.Domain;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.Billing.UseCases;

/// <summary>
/// Provider-agnostic webhook handler. Идемпотентен на <c>ExternalRef</c>
/// (повторный hit того же события — no-op).
///
/// **Не имеет HTTP endpoint** — вызывается провайдер-specific адаптерами
/// после того как они валидируют подпись и нормализуют payload в
/// <see cref="PaymentWebhookRequest"/>. Текущий caller — <c>TBankWebhookHandler</c>.
///
/// Legacy generic endpoint <c>/access/webhooks/payment-confirmation</c> удалён
/// в #124: был anonymous, при пустом <c>WebhookSecret</c> bypass'ил подпись —
/// атакующий с известным OrderId мог выпустить себе grant. Будущие провайдеры
/// (ЮKassa, Stripe) подключаются как separate adapter endpoints с native
/// signature verification, как сейчас сделано для T-Bank.
/// </summary>
public sealed class PaymentWebhookHandler
{
    private readonly IOrdersRepository _orders;
    private readonly IOrderEventsRepository _orderEvents;
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly RenewalFailureRecorder _renewalFailures;
    private readonly PaymentMetrics _metrics;
    private readonly ILogger<PaymentWebhookHandler> _logger;

    public PaymentWebhookHandler(
        IOrdersRepository orders,
        IOrderEventsRepository orderEvents,
        IPlanGrantsRepository grants,
        IPlansRepository plans,
        IOutboxService outbox,
        ITransactionManager transactions,
        RenewalFailureRecorder renewalFailures,
        PaymentMetrics metrics,
        ILogger<PaymentWebhookHandler> logger)
    {
        _orders = orders;
        _orderEvents = orderEvents;
        _grants = grants;
        _plans = plans;
        _outbox = outbox;
        _transactions = transactions;
        _renewalFailures = renewalFailures;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(
        PaymentWebhookRequest request,
        CancellationToken cancellationToken)
    {
        Result<Order, Error> orderResult = await _orders.GetByAsync(
            o => o.Id == request.OrderId,
            cancellationToken);
        if (orderResult.IsFailure) return orderResult.Error;

        Order order = orderResult.Value;
        if (order.ChargeType != OrderChargeType.RENEWAL)
        {
            return await HandleCoreAsync(request, cancellationToken);
        }

        Guid? grantId = order.RenewalGrantId;
        if (grantId is null)
        {
            Result<PlanGrant, Error> activeGrant = await _grants.GetByAsync(
                g => g.UserId == order.UserId
                  && g.PlanId == order.PlanId
                  && g.RebillId == order.RebillId
                  && g.Status == PlanGrantStatus.ACTIVE,
                cancellationToken);
            grantId = activeGrant.IsSuccess ? activeGrant.Value.Id : null;
        }

        if (grantId is null)
        {
            return await HandleCoreAsync(request, cancellationToken);
        }

        IAsyncDisposable? renewalLock = await _orders.TryAcquireRenewalLockAsync(
            grantId.Value,
            cancellationToken);
        if (renewalLock is null)
        {
            bool isRefund = string.Equals(
                request.Status,
                "REFUNDED",
                StringComparison.OrdinalIgnoreCase);
            return Error.Failure(
                isRefund
                    ? "payment.renewal.refund.lock_busy"
                    : "payment.renewal.lock_busy",
                "Renewal transition will be retried after the active charge attempt finishes");
        }

        await using (renewalLock)
        {
            return await HandleCoreAsync(request, cancellationToken);
        }
    }

    private async Task<UnitResult<Error>> HandleCoreAsync(
        PaymentWebhookRequest request,
        CancellationToken cancellationToken)
    {
        string? correlationId = BillingCorrelation.CurrentTraceId();

        Result<Order, Error> orderResult = await _orders.GetByAsync(
            o => o.Id == request.OrderId, cancellationToken);
        if (orderResult.IsFailure)
            return orderResult.Error;

        Order order = orderResult.Value;

        // Idempotency: повторный hit ТОГО ЖЕ webhook'а (тот же status + тот же
        // external_ref + текущий статус совпадает с запрашиваемым) — no-op.
        // НЕ обрабатываем как replay: PAID→REFUNDED transition (тот же ExternalRef,
        // но request.Status сменился) — это легитимный переход, не дубль.
        string requestedStatus = request.Status.ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(request.RebillId))
        {
            UnitResult<Error> attachRebill = order.AttachRebillId(request.RebillId);
            if (attachRebill.IsFailure) return attachRebill.Error;
        }

        bool sameExternalRef = string.Equals(order.ExternalProviderRef, request.ExternalRef, StringComparison.Ordinal);
        bool currentMatchesRequested =
            (string.Equals(requestedStatus, "PAID", StringComparison.Ordinal) && order.Status == OrderStatus.PAID)
            || (string.Equals(requestedStatus, "REFUNDED", StringComparison.Ordinal) && order.Status == OrderStatus.REFUNDED)
            || (string.Equals(requestedStatus, "FAILED", StringComparison.Ordinal) && order.Status == OrderStatus.FAILED);

        if (sameExternalRef && currentMatchesRequested)
        {
            _logger.LogDebug(
                "Payment webhook: idempotent retry for order {OrderId} (status={Status})",
                order.Id, order.Status);
            return UnitResult.Success<Error>();
        }

        // ⚠️ Read-before-mutate: все БД-чтения (план, существующий grant) делаем ДО
        // мутаций domain state. Если чтение упадёт (план удалён / grant.Revoke
        // невалиден) — sub-handler вернёт ошибку, а caller сделает Rollback'нет
        // транзакцию. Иначе MarkPaid/Refund могли бы успеть закоммититься
        // (caller вызовет Commit для записи audit row), а PlanGrant — нет, и юзер
        // заплатил без доступа.
        Plan? planForGrant = null;
        PlanGrant? renewalGrantForTransition = null;
        PlanGrant? activeGrantForInitialConfirmation = null;
        string planTierForMetrics = "unknown";
        bool isRenewalFailure = order.ChargeType == OrderChargeType.RENEWAL
            && string.Equals(requestedStatus, "FAILED", StringComparison.Ordinal);
        bool isInitialConfirmation = order.ChargeType != OrderChargeType.RENEWAL
            && string.Equals(requestedStatus, "PAID", StringComparison.Ordinal);
        if (string.Equals(requestedStatus, "PAID", StringComparison.Ordinal)
            || string.Equals(requestedStatus, "AUTHORIZED", StringComparison.Ordinal)
            || isRenewalFailure)
        {
            // Plan load failure здесь = план не найден (NotFound) — оплата прошла, но grant
            // выпустить не из чего (план удалён, пока заказ был PENDING). НЕ возвращаем ошибку
            // (это вызвало бы rollback + бесконечный retry) — обрабатываем как терминальный
            // GRANT_FAILED ниже, оставляя planForGrant == null.
            Result<Plan, Error> planResult = await _plans.GetByAsync(
                p => p.Id == order.PlanId, cancellationToken);
            if (planResult.IsSuccess)
            {
                planForGrant = planResult.Value;
                planTierForMetrics = planForGrant.Tier.ToString();
            }

            if ((string.Equals(requestedStatus, "PAID", StringComparison.Ordinal)
                    || isRenewalFailure)
                && order.ChargeType == OrderChargeType.RENEWAL
                && planForGrant is not null)
            {
                if (planForGrant.Term.Kind != PlanTermKind.RECURRING
                    || planForGrant.Term.RecurringIntervalDays is not > 0)
                {
                    return Error.Validation(
                        "payment.renewal.plan.invalid",
                        "Renewal order requires a recurring plan");
                }

                Result<PlanGrant, Error> renewalGrantResult = await _grants.GetByAsync(
                    g => g.Id == order.RenewalGrantId
                      && g.UserId == order.UserId
                      && g.PlanId == order.PlanId
                      && (g.Status == PlanGrantStatus.ACTIVE || g.Status == PlanGrantStatus.EXPIRED)
                      && g.RebillId == order.RebillId,
                    cancellationToken);
                if (renewalGrantResult.IsFailure)
                {
                    return renewalGrantResult.Error;
                }

                renewalGrantForTransition = renewalGrantResult.Value;
            }

            if (isInitialConfirmation && planForGrant is not null)
            {
                Result<PlanGrant, Error> activeGrantResult = await _grants.GetActiveForUpdateAsync(
                    order.UserId,
                    order.PlanId,
                    cancellationToken);
                activeGrantForInitialConfirmation = activeGrantResult.IsSuccess
                    ? activeGrantResult.Value
                    : null;
            }
        }

        switch (requestedStatus)
        {
            case "AUTHORIZED":
                {
                    // T-Bank sends RebillId on AUTHORIZED, while access is issued only on
                    // CONFIRMED. Persist it on Order. If CONFIRMED won the delivery race,
                    // attach the saved token to the already-created subscription grant too.
                    if (planForGrant?.Tier == PlanTier.SUBSCRIPTION
                        && order.RebillId is { } rebillId)
                    {
                        Result<PlanGrant, Error> grantResult = await _grants.GetByAsync(
                            g => g.UserId == order.UserId
                              && g.PlanId == order.PlanId
                              && g.Status == PlanGrantStatus.ACTIVE
                              && g.Source == PlanGrantSource.PURCHASE
                              && g.SourceRef == order.Id,
                            cancellationToken);

                        if (grantResult.IsSuccess && grantResult.Value.ExpiresAt is { } expiresAt)
                        {
                            UnitResult<Error> attach = grantResult.Value.AttachRecurring(
                                rebillId,
                                order.UserId.ToString(),
                                SubscriptionRenewalPolicy.FirstChargeAt(expiresAt));
                            if (attach.IsFailure) return attach.Error;
                        }
                    }

                    break;
                }
            case "PAID":
                {
                    // Оплата прошла, но плана для выдачи нет (удалён, пока заказ был PENDING) —
                    // терминальный GRANT_FAILED: помечаем заказ FAILED (честное «оплачено, доступ
                    // не выдан»), пишем audit-row для админки, грант не выпускаем. Возврат — вручную.
                    // ТОЛЬКО на PENDING-заказе: на already-terminal (reconciliation race / повторный
                    // webhook на FAILED/PAID-заказе) не плодим дубль audit-row и не пытаемся
                    // MarkFailed на не-PENDING статусе (вернул бы ошибку → rollback → retry-loop).
                    if (planForGrant is null)
                    {
                        if (order.Status == OrderStatus.PENDING)
                        {
                            UnitResult<Error> markFailed = order.MarkFailed("grant_failed: plan not found");
                            if (markFailed.IsFailure) return markFailed.Error;

                            await _orderEvents.AddAsync(
                                OrderEvent.Record(
                                    order.Id,
                                    OrderEventType.GRANT_FAILED,
                                    JsonSerializer.Serialize(new { reason = "plan.not.found", planId = order.PlanId }),
                                    correlationId: correlationId),
                                cancellationToken);
                            _logger.LogError(
                                "Payment webhook: order {OrderId} PAID but plan {PlanId} not found — GRANT_FAILED",
                                order.Id, order.PlanId);
                        }

                        break;
                    }

                    UnitResult<Error> mark = order.MarkPaid(request.ExternalRef);
                    if (mark.IsFailure) return mark.Error;

                    // RENEWAL is applied here as the single durable completion path for both
                    // the sweeper response and provider webhook/reconciliation. This closes the
                    // crash window after Charge CONFIRMED but before local grant persistence.
                    if (order.ChargeType == OrderChargeType.RENEWAL)
                    {
                        PlanGrant grant = renewalGrantForTransition!;
                        Plan plan = planForGrant!;
                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        DateTimeOffset extendBase = grant.ExpiresAt is { } expiresAt && expiresAt > now
                            ? expiresAt
                            : now;
                        DateTimeOffset newExpiresAt = extendBase.AddDays(
                            plan.Term.RecurringIntervalDays!.Value);
                        int successfulAttempt = grant.ChargeFailureCount + 1;

                        if (grant.ExpiresAt is not { } previousExpiresAt)
                        {
                            return Error.Validation(
                                "payment.renewal.grant_expiry_missing",
                                "Renewal grant must have ExpiresAt");
                        }

                        UnitResult<Error> snapshot = order.RecordRenewalPeriod(
                            grant.Id,
                            previousExpiresAt,
                            newExpiresAt);
                        if (snapshot.IsFailure) return snapshot.Error;

                        UnitResult<Error> renew = grant.Renew(
                            newExpiresAt,
                            newNextChargeAt: SubscriptionRenewalPolicy.FirstChargeAt(newExpiresAt));
                        if (renew.IsFailure) return renew.Error;

                        await _outbox.PublishAsync(new PlanGrantRenewed(
                            grant.Id,
                            grant.UserId,
                            grant.PlanId,
                            plan.Tier.ToString(),
                            plan.AuthorId,
                            newExpiresAt,
                            order.Id,
                            now,
                            previousExpiresAt,
                            grant.NextChargeAt,
                            grant.RenewalGraceEndsAt,
                            successfulAttempt));
                        break;
                    }

                    // Idempotency: если у юзера уже есть active grant с этим
                    // SourceRef = order.Id — не плодим дубль (повторный webhook). Scoping
                    // по orderId критичен для случая «один user купил два разных order'а
                    // на тот же план» — иначе второй grant скипнется.
                    bool alreadyGranted = activeGrantForInitialConfirmation is
                    {
                        Source: PlanGrantSource.PURCHASE,
                        SourceRef: var sourceRef,
                    } && sourceRef == order.Id;

                    if (!alreadyGranted && activeGrantForInitialConfirmation is { } activeGrant)
                    {
                        await _orderEvents.AddAsync(
                            OrderEvent.Record(
                                order.Id,
                                OrderEventType.GRANT_FAILED,
                                JsonSerializer.Serialize(new
                                {
                                    reason = "active_grant_exists",
                                    action = "manual_review",
                                    existingGrantId = activeGrant.Id,
                                    existingGrantSource = activeGrant.Source.ToString(),
                                    existingGrantSourceRef = activeGrant.SourceRef,
                                    planId = activeGrant.PlanId,
                                }),
                                correlationId: correlationId),
                            cancellationToken);

                        _logger.LogError(
                            "Payment webhook: confirmed order {OrderId} found different active grant {GrantId}; order recorded PAID for manual duplicate-payment review",
                            order.Id,
                            activeGrant.Id);
                    }

                    if (!alreadyGranted)
                    {
                        if (activeGrantForInitialConfirmation is not null)
                        {
                            break;
                        }

                        Plan plan = planForGrant;
                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        // TTL-грант:
                        //  - Подписка (#614): срок = Term.RecurringIntervalDays; по истечении
                        //    ожидается автопродление (или ExpiredGrantsSweeper закроет доступ,
                        //    если списание не прошло). Имеет приоритет над trial-веткой —
                        //    SUBSCRIPTION-планы не trial.
                        //  - Trial (#580): TTL = TrialDurationDays дней.
                        //  - Иначе бессрочный план → expiresAt = null (как и раньше).
                        bool isSubscription = plan.Tier == PlanTier.SUBSCRIPTION
                            && plan.Term.RecurringIntervalDays is > 0;
                        DateTimeOffset? expiresAt = isSubscription
                            ? now.AddDays(plan.Term.RecurringIntervalDays!.Value)
                            : plan.TrialDurationDays is int trialDays
                                ? now.AddDays(trialDays)
                                : null;
                        long? upgradeBasePriceCents = await ResolveTrialUpgradeBasePriceCentsAsync(
                            plan,
                            now,
                            cancellationToken);

                        // PricePaidCents = order.AmountCents — финальная сумма,
                        // которую юзер реально заплатил (после применения credit'а
                        // в момент создания order'а). Phase 2 #112 — служит снимком
                        // для будущих upgrade-расчётов.
                        PlanGrant grant = PlanGrant.Create(
                            order.UserId,
                            order.PlanId,
                            PlanGrantSource.PURCHASE,
                            sourceRef: order.Id,
                            expiresAt: expiresAt,
                            pricePaidCents: order.AmountCents,
                            upgradeBasePriceCents: upgradeBasePriceCents);
                        await _grants.AddAsync(grant, cancellationToken);

                        // Подписка (#614): сохраняем recurring-токены провайдера на grant'е,
                        // чтобы sweeper (A2b) мог провести безредиректное автосписание на
                        // следующем NextChargeAt (= ExpiresAt − 24ч согласно lifecycle #746).
                        // CustomerKey = строка userId (тот же
                        // стабильный ключ, что мы передавали в Init.CustomerKey). Если RebillId
                        // отсутствует — деградируем до ручного продления: warning, но grant создаём.
                        if (isSubscription)
                        {
                            if (order.RebillId is { } rebillId && expiresAt is { } subExpiresAt)
                            {
                                UnitResult<Error> attach = grant.AttachRecurring(
                                    rebillId,
                                    customerKey: order.UserId.ToString(),
                                    nextChargeAt: SubscriptionRenewalPolicy.FirstChargeAt(subExpiresAt));
                                if (attach.IsFailure) return attach.Error;
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Payment webhook: subscription order {OrderId} confirmed without RebillId — grant {GrantId} created without auto-renew (manual renewal)",
                                    order.Id, grant.Id);
                            }
                        }

                        await _outbox.PublishAsync(new PlanGrantCreated(
                            grant.Id,
                            grant.UserId,
                            plan.Id,
                            plan.Tier.ToString(),
                            plan.AuthorId,
                            plan.FirstCourseId,
                            plan.IncludesFutureContent,
                            grant.Source.ToString(),
                            grant.SourceRef,
                            grant.GrantedAt,
                            grant.ExpiresAt,
                            PlanCapabilitiesMapper.ToStrings(plan.Capabilities),
                            [.. plan.Courses.Select(c => c.CourseId)],
                            plan.DisplayName.Value,
                            plan.OfferType.ToString()));

                        // Audit: grant успешно выпущен — idempotent (только на НОВЫЙ grant,
                        // повторный webhook сюда не входит).
                        await _orderEvents.AddAsync(
                            OrderEvent.Record(
                                order.Id,
                                OrderEventType.GRANT_ISSUED,
                                JsonSerializer.Serialize(new
                                {
                                    grantId = grant.Id,
                                    planId = plan.Id,
                                    source = grant.Source.ToString(),
                                }),
                                correlationId: correlationId),
                            cancellationToken);
                    }
                    break;
                }
            case "FAILED":
                {
                    if (order.ChargeType == OrderChargeType.RENEWAL)
                    {
                        if (planForGrant is null || renewalGrantForTransition is null)
                        {
                            return Error.Failure(
                                "payment.renewal.context_missing",
                                "Renewal failure requires its plan and grant");
                        }

                        DateTimeOffset failedAt = DateTimeOffset.UtcNow;
                        bool lifecycleAlreadyClosed =
                            renewalGrantForTransition.Status != PlanGrantStatus.ACTIVE
                            || renewalGrantForTransition.AutoRenewalCancelledAt is not null
                            || renewalGrantForTransition.AccessEndsAt <= failedAt;
                        if (lifecycleAlreadyClosed)
                        {
                            // The charge attempt may have remained PENDING while the user
                            // cancelled renewal or the hard grace boundary elapsed. Converge
                            // the audit order, but never restart dunning or extend access.
                            UnitResult<Error> closeOrder = order.MarkFailed(request.Reason);
                            if (closeOrder.IsFailure) return closeOrder.Error;
                            break;
                        }

                        Result<RenewalFailureTransition, Error> failure =
                            await _renewalFailures.RecordAsync(
                                renewalGrantForTransition,
                                planForGrant,
                                order,
                                request.Reason,
                                failedAt);
                        if (failure.IsFailure) return failure.Error;
                        break;
                    }

                    UnitResult<Error> mark = order.MarkFailed(request.Reason);
                    if (mark.IsFailure) return mark.Error;
                    break;
                }
            case "REFUNDED":
                {
                    if (order.ChargeType == OrderChargeType.RENEWAL)
                    {
                        Guid? renewalGrantId = order.RenewalGrantId;
                        DateTimeOffset? snapshotPrevious = order.RenewalPreviousExpiresAt;
                        DateTimeOffset? snapshotTarget = order.RenewalTargetExpiresAt;
                        if (renewalGrantId is null
                            || snapshotPrevious is null
                            || snapshotTarget is null)
                        {
                            // Provider may move INIT/Charge all the way to REFUNDED while
                            // both PAID notifications are lost. Local entitlement was never
                            // extended, so there is nothing to roll back; converge only Order.
                            if (order.Status == OrderStatus.PENDING)
                            {
                                Result<PlanGrant, Error> pendingGrantResult = await _grants.GetByAsync(
                                    g => g.UserId == order.UserId
                                      && g.PlanId == order.PlanId
                                      && g.RebillId == order.RebillId
                                      && (order.RenewalGrantId != null
                                          ? g.Id == order.RenewalGrantId
                                          : g.Status == PlanGrantStatus.ACTIVE)
                                      && (g.Status == PlanGrantStatus.ACTIVE || g.Status == PlanGrantStatus.EXPIRED),
                                    cancellationToken);

                                PlanGrant? pendingGrant = pendingGrantResult.IsSuccess
                                    ? pendingGrantResult.Value
                                    : null;
                                Guid? canonicalPlanId = null;
                                if (pendingGrant is not null)
                                {
                                    Result<Guid?, Error> canonicalResult =
                                        await CanonicalTelegramPlanResolver.ResolveAsync(
                                            pendingGrant.PlanId,
                                            _plans,
                                            cancellationToken);
                                    if (canonicalResult.IsFailure) return canonicalResult.Error;
                                    canonicalPlanId = canonicalResult.Value;
                                }

                                UnitResult<Error> pendingRefund = order.Refund(request.Reason);
                                if (pendingRefund.IsFailure) return pendingRefund.Error;

                                if (pendingGrant is not null)
                                {
                                    DateTimeOffset expiresAt = pendingGrant.ExpiresAt ?? DateTimeOffset.UtcNow;
                                    DateTimeOffset pendingRefundedAt = DateTimeOffset.UtcNow;
                                    UnitResult<Error> stopAutoRenew = pendingGrant.StopAutoRenewal(pendingRefundedAt);
                                    if (stopAutoRenew.IsFailure) return stopAutoRenew.Error;
                                    await _outbox.PublishAsync(new PlanGrantRenewalRefunded(
                                        pendingGrant.Id,
                                        pendingGrant.UserId,
                                        pendingGrant.PlanId,
                                        order.Id,
                                        expiresAt,
                                        expiresAt,
                                        pendingRefundedAt,
                                        request.Reason,
                                        canonicalPlanId));
                                }

                                break;
                            }

                            Result<Plan, Error> legacyPlanResult = await _plans.GetByAsync(
                                p => p.Id == order.PlanId,
                                cancellationToken);
                            if (legacyPlanResult.IsFailure
                                || legacyPlanResult.Value.Term.RecurringIntervalDays is not > 0)
                            {
                                return Error.Conflict(
                                    "payment.renewal.refund.legacy_plan_invalid",
                                    "Legacy renewal refund cannot derive the paid period");
                            }

                            IReadOnlyList<PlanGrant> legacyCandidates = await _grants.GetManyByAsync(
                                g => g.UserId == order.UserId
                                  && g.PlanId == order.PlanId
                                  && g.RebillId == order.RebillId
                                  && (g.Status == PlanGrantStatus.ACTIVE || g.Status == PlanGrantStatus.EXPIRED),
                                cancellationToken);
                            if (legacyCandidates.Count != 1)
                            {
                                return Error.Conflict(
                                    "payment.renewal.refund.legacy_grant_ambiguous",
                                    "Legacy renewal refund requires exactly one matching grant");
                            }

                            renewalGrantId = legacyCandidates[0].Id;
                            snapshotPrevious = order.PaidAt ?? order.CreatedAt;
                            snapshotTarget = snapshotPrevious.Value.AddDays(
                                legacyPlanResult.Value.Term.RecurringIntervalDays.Value);
                            UnitResult<Error> legacySnapshot = order.RecordRenewalPeriod(
                                renewalGrantId.Value,
                                snapshotPrevious.Value,
                                snapshotTarget.Value);
                            if (legacySnapshot.IsFailure) return legacySnapshot.Error;
                        }

                        Result<PlanGrant, Error> renewalGrantResult = await _grants.GetByAsync(
                            g => g.Id == renewalGrantId.Value
                              && g.UserId == order.UserId
                              && g.PlanId == order.PlanId
                              && (g.Status == PlanGrantStatus.ACTIVE || g.Status == PlanGrantStatus.EXPIRED),
                            cancellationToken);
                        if (renewalGrantResult.IsFailure) return renewalGrantResult.Error;

                        PlanGrant renewalGrant = renewalGrantResult.Value;
                        if (renewalGrant.ExpiresAt is not { } beforeRollback)
                        {
                            return Error.Conflict(
                                "payment.renewal.refund.grant_expiry_missing",
                                "Renewal grant has no expiry to roll back");
                        }

                        Result<Guid?, Error> canonicalPlanResult =
                            await CanonicalTelegramPlanResolver.ResolveAsync(
                                renewalGrant.PlanId,
                                _plans,
                                cancellationToken);
                        if (canonicalPlanResult.IsFailure) return canonicalPlanResult.Error;

                        UnitResult<Error> refundRenewal = order.Refund(request.Reason);
                        if (refundRenewal.IsFailure) return refundRenewal.Error;

                        Result<DateTimeOffset, Error> rollback = renewalGrant.RollbackRenewal(
                            snapshotTarget.Value - snapshotPrevious.Value,
                            DateTimeOffset.UtcNow);
                        if (rollback.IsFailure) return rollback.Error;

                        DateTimeOffset refundedAt = DateTimeOffset.UtcNow;
                        await _outbox.PublishAsync(new PlanGrantRenewalRefunded(
                            renewalGrant.Id,
                            renewalGrant.UserId,
                            renewalGrant.PlanId,
                            order.Id,
                            beforeRollback,
                            rollback.Value,
                            refundedAt,
                            request.Reason,
                            canonicalPlanResult.Value));
                        break;
                    }

                    // Read-before-mutate: ищем существующий grant ПОСЛЕ Order.Refund() ниже
                    // через выполнение этого read'а здесь, ДО любой мутации. Order.Refund()
                    // вызывается дальше; grant.Revoke() — только если grant найден. Если
                    // Revoke провалится (статус не ACTIVE, race) — handler возвращает ошибку,
                    // caller откатывает транзакцию, и Order НЕ остаётся REFUNDED без revoke'а.
                    //
                    // Grant мог быть выпущен двумя путями на этот order: PURCHASE (обычная
                    // оплата через webhook PAID) ИЛИ ADMIN_GRANT (саппорт выдал вручную через
                    // grant-manually, если клиент заплатил вне системы). Refund должен снять
                    // доступ независимо от способа выдачи — ищем оба source'а по SourceRef.
                    Result<PlanGrant, Error> existingGrant = await _grants.GetByAsync(
                        g => g.UserId == order.UserId
                          && g.PlanId == order.PlanId
                          && g.Status == PlanGrantStatus.ACTIVE
                          && (g.Source == PlanGrantSource.PURCHASE || g.Source == PlanGrantSource.ADMIN_GRANT)
                          && g.SourceRef == order.Id,
                        cancellationToken);

                    UnitResult<Error> refund = order.Refund(request.Reason);
                    if (refund.IsFailure) return refund.Error;

                    if (existingGrant.IsSuccess)
                    {
                        PlanGrant grant = existingGrant.Value;
                        Result<Guid?, Error> canonicalPlanResult =
                            await CanonicalTelegramPlanResolver.ResolveAsync(
                                grant.PlanId,
                                _plans,
                                cancellationToken);
                        if (canonicalPlanResult.IsFailure)
                            return canonicalPlanResult.Error;

                        UnitResult<Error> revokeResult = grant.Revoke(
                            order.UserId, $"refund: {request.Reason ?? "no reason"}");
                        if (revokeResult.IsFailure) return revokeResult.Error;

                        // КРИТИЧНО: publish PlanGrantRevoked в outbox — без этого
                        // self-consume recalc handler (#79) не сработает и Redis
                        // plan-tags останутся, юзер сохранит доступ навсегда.
                        await _outbox.PublishAsync(new PlanGrantRevoked(
                            grant.Id,
                            grant.UserId,
                            grant.PlanId,
                            $"refund: {request.Reason ?? "no reason"}",
                            grant.RevokedAt!.Value,
                            canonicalPlanResult.Value));
                    }
                    break;
                }
            default:
                return Error.Validation("payment.status.invalid",
                    $"Unknown payment status: {request.Status}");
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
            return save.Error;

        // Метрики после успешного save (без extra DB round-trip — план уже
        // загружен на read-before-mutate шаге).
        string provider = order.Provider ?? "unknown";
        switch (requestedStatus)
        {
            case "PAID":
                if (order.Status == OrderStatus.PAID)
                {
                    _metrics.RecordOrderPaid(provider, planTierForMetrics);
                    if (order.PaidAt is { } paidAt)
                    {
                        TimeSpan e2e = paidAt - order.CreatedAt;
                        _metrics.RecordE2eDuration(provider, planTierForMetrics, e2e.TotalSeconds);
                    }
                }
                else
                {
                    // GRANT_FAILED ветка — заказ FAILED, не PAID.
                    _metrics.RecordOrderFailed(provider, "grant_failed");
                }
                break;
            case "FAILED":
                _metrics.RecordOrderFailed(provider, FailureReasonNormalizer.ToMetricBucket(request.Reason));
                break;
        }

        _logger.LogInformation(
            "Payment webhook processed: order {OrderId} → {Status} (ref={Ref})",
            order.Id, request.Status, request.ExternalRef);
        return UnitResult.Success<Error>();
    }

    private async Task<long?> ResolveTrialUpgradeBasePriceCentsAsync(
        Plan plan,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!plan.IsTrial || plan.Tier != PlanTier.FULL_ALL)
        {
            return null;
        }

        IReadOnlyList<Plan> candidates = await _plans.GetManyByAsync(
            p => p.Tier == PlanTier.FULL_ALL
              && p.TrialDurationDays == null
              && p.ArchivedAt == null
              && p.IsActive
              && p.IsPublic,
            ct);

        Plan? target = candidates
            .Where(p => p.EffectivePriceCents(now) is > 0)
            .OrderByDescending(p => p.IsHighlighted)
            .ThenBy(p => p.DisplayOrder)
            .ThenBy(p => p.CreatedAt)
            .FirstOrDefault();

        return target?.EffectivePriceCents(now);
    }
}
