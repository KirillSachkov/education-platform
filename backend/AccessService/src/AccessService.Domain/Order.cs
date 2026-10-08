using SharedKernel.DomainEvents;

namespace AccessService.Domain;

/// <summary>
/// Aggregate root: заказ на план. Создаётся фронтом при инициации checkout-flow,
/// подтверждается webhook'ом платёжного провайдера. После PAID → выпуск
/// <see cref="PlanGrant"/> (Source=PURCHASE).
///
/// Issue #85 — billing scaffold. Provider-agnostic: <see cref="ExternalProviderRef"/>
/// — это ID транзакции провайдера (ЮKassa payment.id, Stripe pi_xxx и т.п.).
/// Используется для idempotency на webhook retry'ях.
///
/// В будущем может быть extracted в отдельный BillingService микросервис.
/// </summary>
public sealed class Order : AggregateRoot
{
    public const int CURRENCY_MAX_LENGTH = 3;
    public const int PROVIDER_MAX_LENGTH = 50;
    public const int EXTERNAL_REF_MAX_LENGTH = 200;
    public const int FAILURE_REASON_MAX_LENGTH = 500;
    public const int CORRELATION_ID_MAX_LENGTH = 64;
    public const int REBILL_ID_MAX_LENGTH = 100;

    private Order() { } // EF

    private Order(
        Guid id,
        Guid userId,
        Guid planId,
        long amountCents,
        string currency,
        string? provider,
        string? correlationId,
        OrderChargeType chargeType,
        string? rebillId,
        DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        PlanId = planId;
        AmountCents = amountCents;
        Currency = currency;
        Provider = provider;
        CorrelationId = correlationId;
        ChargeType = chargeType;
        RebillId = rebillId;
        CreatedAt = createdAt;
        Status = OrderStatus.PENDING;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid PlanId { get; private set; }

    /// <summary>Сумма в копейках. <c>long</c> — int.MaxValue это ~21.4M ₽ за заказ,
    /// узкое место для b2b-планов.</summary>
    public long AmountCents { get; private set; }

    public string Currency { get; private set; } = "RUB";

    /// <summary>
    /// Идентификатор транзакции у провайдера (ЮKassa payment_id, Stripe payment_intent_id).
    /// Заполняется на webhook'е PAID — используется для idempotency.
    /// </summary>
    public string? ExternalProviderRef { get; private set; }

    /// <summary>Имя провайдера ("yukassa", "stripe", ...). Информационное.</summary>
    public string? Provider { get; private set; }

    /// <summary>
    /// OTel trace_id (<c>Activity.Current?.TraceId</c>) запроса, создавшего заказ. Связывает
    /// заказ с логами/трейсами checkout-flow для admin-расследований; по нему фильтрует
    /// <c>GET /access/admin/orders/?correlationId=</c> (#443).
    /// </summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// Тип списания (#614): <see cref="OrderChargeType.INITIAL"/> — первый платёж через
    /// checkout-redirect; <see cref="OrderChargeType.RENEWAL"/> — серверное автосписание
    /// по подписке (создаётся через <see cref="CreateRenewal"/>). Дефолт INITIAL.
    /// </summary>
    public OrderChargeType ChargeType { get; private set; }

    /// <summary>
    /// Recurring-токен провайдера (T-Bank RebillId, #614). Для INITIAL subscription-order
    /// сохраняется из AUTHORIZED webhook до CONFIRMED; для RENEWAL указывает token,
    /// по которому проведено безредиректное списание.
    /// </summary>
    public string? RebillId { get; private set; }

    /// <summary>Audit snapshot of the entitlement mutation produced by a renewal payment.</summary>
    public Guid? RenewalGrantId { get; private set; }

    public DateTimeOffset? RenewalPreviousExpiresAt { get; private set; }

    public DateTimeOffset? RenewalTargetExpiresAt { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public string? FailureReason { get; private set; }

    public static Result<Order, Error> Create(
        Guid userId,
        Guid planId,
        long amountCents,
        string currency,
        string? provider = null,
        string? correlationId = null)
    {
        if (userId == Guid.Empty)
            return Error.Validation("order.user.empty", "UserId не может быть пустым");
        if (planId == Guid.Empty)
            return Error.Validation("order.plan.empty", "PlanId не может быть пустым");
        if (amountCents <= 0)
            return Error.Validation("order.amount.invalid", "Сумма должна быть положительной");

        string normalizedCurrency = (currency ?? "RUB").Trim().ToUpperInvariant();
        if (normalizedCurrency.Length is < 3 or > CURRENCY_MAX_LENGTH)
            return Error.Validation("order.currency.invalid", "Currency должен быть ISO 4217 (3 буквы)");

        if (provider is { Length: > PROVIDER_MAX_LENGTH })
            return Error.Validation("order.provider.too_long", "Имя провайдера слишком длинное");

        string? normalizedCorrelationId = correlationId is { Length: > CORRELATION_ID_MAX_LENGTH }
            ? correlationId[..CORRELATION_ID_MAX_LENGTH]
            : correlationId;

        Order order = new(
            Guid.CreateVersion7(),
            userId,
            planId,
            amountCents,
            normalizedCurrency,
            provider,
            normalizedCorrelationId,
            OrderChargeType.INITIAL,
            rebillId: null,
            DateTimeOffset.UtcNow);

        return order;
    }

    /// <summary>
    /// Создаёт RENEWAL-заказ для серверного автосписания по подписке (#614) — без редиректа,
    /// по сохранённому <paramref name="rebillId"/>. Сразу PENDING; webhook/charge-result переводит
    /// в PAID/FAILED тем же путём, что и обычный заказ. Issue #614 / workstream A2 проводит списание.
    /// </summary>
    public static Result<Order, Error> CreateRenewal(
        Guid userId,
        Guid planId,
        Guid renewalGrantId,
        long amountCents,
        string currency,
        string rebillId,
        string? provider = null,
        string? correlationId = null)
    {
        if (userId == Guid.Empty)
            return Error.Validation("order.user.empty", "UserId не может быть пустым");
        if (planId == Guid.Empty)
            return Error.Validation("order.plan.empty", "PlanId не может быть пустым");
        if (renewalGrantId == Guid.Empty)
            return Error.Validation("order.renewal_grant.empty", "RenewalGrantId обязателен для автосписания");
        if (amountCents <= 0)
            return Error.Validation("order.amount.invalid", "Сумма должна быть положительной");
        if (string.IsNullOrWhiteSpace(rebillId))
            return Error.Validation("order.rebill_id.empty", "RebillId обязателен для автосписания");
        if (rebillId.Length > REBILL_ID_MAX_LENGTH)
            return Error.Validation("order.rebill_id.too_long", "RebillId слишком длинный");

        string normalizedCurrency = (currency ?? "RUB").Trim().ToUpperInvariant();
        if (normalizedCurrency.Length is < 3 or > CURRENCY_MAX_LENGTH)
            return Error.Validation("order.currency.invalid", "Currency должен быть ISO 4217 (3 буквы)");

        if (provider is { Length: > PROVIDER_MAX_LENGTH })
            return Error.Validation("order.provider.too_long", "Имя провайдера слишком длинное");

        string? normalizedCorrelationId = correlationId is { Length: > CORRELATION_ID_MAX_LENGTH }
            ? correlationId[..CORRELATION_ID_MAX_LENGTH]
            : correlationId;

        Order order = new(
            Guid.CreateVersion7(),
            userId,
            planId,
            amountCents,
            normalizedCurrency,
            provider,
            normalizedCorrelationId,
            OrderChargeType.RENEWAL,
            rebillId,
            DateTimeOffset.UtcNow);
        order.RenewalGrantId = renewalGrantId;

        return order;
    }

    /// <summary>
    /// Привязывает <c>ExternalProviderRef</c> сразу после успешного Init (до получения
    /// webhook'а). Идемпотентно для того же ref'а — конфликт для другого ref'а.
    /// </summary>
    public UnitResult<Error> AttachExternalRef(string externalRef)
    {
        if (string.IsNullOrWhiteSpace(externalRef))
            return Error.Validation("order.external_ref.empty", "ExternalRef обязателен");
        if (externalRef.Length > EXTERNAL_REF_MAX_LENGTH)
            return Error.Validation("order.external_ref.too_long", "ExternalRef слишком длинный");

        if (ExternalProviderRef is not null)
        {
            return string.Equals(ExternalProviderRef, externalRef, StringComparison.Ordinal)
                ? UnitResult.Success<Error>()
                : Error.Validation("order.external_ref.conflict",
                    $"ExternalRef уже установлен ({ExternalProviderRef}), не может быть изменён на {externalRef}");
        }

        ExternalProviderRef = externalRef;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> RecordRenewalPeriod(
        Guid grantId,
        DateTimeOffset previousExpiresAt,
        DateTimeOffset targetExpiresAt)
    {
        if (ChargeType != OrderChargeType.RENEWAL)
            return Error.Validation("order.renewal_snapshot.not_renewal", "Snapshot допустим только для renewal-заказа");
        if (grantId == Guid.Empty || targetExpiresAt <= previousExpiresAt)
            return Error.Validation("order.renewal_snapshot.invalid", "Некорректный snapshot renewal-периода");

        if (RenewalGrantId is not null)
        {
            if (RenewalGrantId != grantId)
                return Error.Conflict("order.renewal_snapshot.conflict", "Renewal snapshot уже привязан к другому grant");
            if (RenewalPreviousExpiresAt is null && RenewalTargetExpiresAt is null)
            {
                RenewalPreviousExpiresAt = previousExpiresAt;
                RenewalTargetExpiresAt = targetExpiresAt;
                return UnitResult.Success<Error>();
            }

            return RenewalPreviousExpiresAt == previousExpiresAt
                && RenewalTargetExpiresAt == targetExpiresAt
                ? UnitResult.Success<Error>()
                : Error.Conflict("order.renewal_snapshot.conflict", "Renewal snapshot уже записан с другими значениями");
        }

        RenewalGrantId = grantId;
        RenewalPreviousExpiresAt = previousExpiresAt;
        RenewalTargetExpiresAt = targetExpiresAt;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Сохраняет recurring token из AUTHORIZED webhook. Идемпотентно для того же значения;
    /// другой token для уже привязанного заказа считается конфликтом.
    /// </summary>
    public UnitResult<Error> AttachRebillId(string rebillId)
    {
        if (string.IsNullOrWhiteSpace(rebillId))
            return Error.Validation("order.rebill_id.empty", "RebillId обязателен");
        if (rebillId.Length > REBILL_ID_MAX_LENGTH)
            return Error.Validation("order.rebill_id.too_long", "RebillId слишком длинный");

        if (RebillId is not null)
        {
            return string.Equals(RebillId, rebillId, StringComparison.Ordinal)
                ? UnitResult.Success<Error>()
                : Error.Validation("order.rebill_id.conflict", "RebillId уже привязан к заказу");
        }

        RebillId = rebillId;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Подтверждает оплату. Идемпотентно для уже PAID с тем же <paramref name="externalRef"/>
    /// (повторный hit того же webhook'а).
    /// </summary>
    public UnitResult<Error> MarkPaid(string externalRef)
    {
        if (string.IsNullOrWhiteSpace(externalRef))
            return Error.Validation("order.external_ref.empty", "ExternalRef обязателен для PAID");
        if (externalRef.Length > EXTERNAL_REF_MAX_LENGTH)
            return Error.Validation("order.external_ref.too_long", "ExternalRef слишком длинный");

        if (Status == OrderStatus.PAID)
        {
            // Idempotent retry of the same webhook — already PAID, no-op. We intentionally
            // do NOT compare ExternalRef here: T-Bank re-sends CONFIRMED with the same
            // PaymentId, and the webhook adapter already rejects a *different* PaymentId
            // as a replay (WEBHOOK_REJECTED_REPLAY) before reaching this branch.
            return UnitResult.Success<Error>();
        }

        bool isLateProviderConfirmation = Status == OrderStatus.FAILED
            && string.Equals(FailureReason, "reconciliation_expired", StringComparison.Ordinal);
        if (Status != OrderStatus.PENDING && !isLateProviderConfirmation)
            return Error.Validation("order.status.not_pending",
                $"Невозможно перевести из {Status} в PAID");

        Status = OrderStatus.PAID;
        PaidAt = DateTimeOffset.UtcNow;
        ExternalProviderRef = externalRef;
        FailureReason = null;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Помечает заказ FAILED. Идемпотентно для уже FAILED (повторный REJECTED-webhook
    /// или reconciliation на том же заказе) — возвращает success без мутации, зеркаля
    /// already-PAID ветку <see cref="MarkPaid"/>.
    /// </summary>
    public UnitResult<Error> MarkFailed(string? reason)
    {
        if (Status == OrderStatus.FAILED)
        {
            return UnitResult.Success<Error>();
        }

        if (Status != OrderStatus.PENDING)
            return Error.Validation("order.status.not_pending",
                $"Невозможно перевести из {Status} в FAILED");

        Status = OrderStatus.FAILED;
        FailureReason = reason is { Length: > FAILURE_REASON_MAX_LENGTH }
            ? reason[..FAILURE_REASON_MAX_LENGTH]
            : reason;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Refund(string? reason)
    {
        // Provider may already report REFUNDED while the local order is still PENDING
        // because both PAID and REFUNDED notifications were lost. Reconciliation must be
        // able to converge directly to the provider's terminal state.
        if (Status is not (OrderStatus.PAID or OrderStatus.PENDING))
            return Error.Validation("order.status.not_paid",
                $"Невозможно refund из статуса {Status}");

        Status = OrderStatus.REFUNDED;
        FailureReason = reason is { Length: > FAILURE_REASON_MAX_LENGTH }
            ? reason[..FAILURE_REASON_MAX_LENGTH]
            : reason;

        return UnitResult.Success<Error>();
    }
}
