namespace AccessService.Domain;

/// <summary>
/// Append-only audit-запись для <see cref="Order"/>: каждый webhook, каждый
/// state-transition, каждое admin-действие. Используется для расследования
/// проблем с платежами и для compliance (РКН/ЦБ при проверке).
///
/// Phase F.1.0 — введён в issue #102. correlation_id (#443) связывает row
/// с логами/трейсами через OTel trace_id.
/// </summary>
public sealed class OrderEvent
{
    public const int EVENT_TYPE_MAX_LENGTH = 50;
    public const int CORRELATION_ID_MAX_LENGTH = 64;

    private OrderEvent() { } // EF

    private OrderEvent(
        Guid id,
        Guid orderId,
        OrderEventType eventType,
        string? payloadJson,
        Guid? actorUserId,
        string? correlationId,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrderId = orderId;
        EventType = eventType;
        PayloadJson = payloadJson;
        ActorUserId = actorUserId;
        CorrelationId = correlationId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public OrderEventType EventType { get; private set; }

    /// <summary>JSON payload — raw webhook body / API response / admin reason. Nullable.</summary>
    public string? PayloadJson { get; private set; }

    /// <summary>UserId админа для MANUAL_GRANT/REVOKE/REFUND, иначе null.</summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>
    /// OTel trace_id (<c>Activity.Current?.TraceId</c>) запроса/процесса, в котором
    /// записано событие. Связывает audit-row с логами и трейсами при расследовании
    /// «оплатил, но доступа нет». Nullable — фоновые пути без активного Activity
    /// (часть reconciliation-tick'ов) пишут row без correlation.
    /// </summary>
    public string? CorrelationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static OrderEvent Record(
        Guid orderId,
        OrderEventType eventType,
        string? payloadJson = null,
        Guid? actorUserId = null,
        string? correlationId = null) => new(
            Guid.CreateVersion7(),
            orderId,
            eventType,
            payloadJson,
            actorUserId,
            Truncate(correlationId),
            DateTimeOffset.UtcNow);

    private static string? Truncate(string? correlationId) =>
        correlationId is { Length: > CORRELATION_ID_MAX_LENGTH }
            ? correlationId[..CORRELATION_ID_MAX_LENGTH]
            : correlationId;
}

/// <summary>
/// Категории audit-событий по <see cref="Order"/>.
/// </summary>
public enum OrderEventType
{
    /// <summary>Перед вызовом провайдерского Init (создание платёжной сессии).</summary>
    INIT_CALLED,

    /// <summary>Провайдер вернул успешный Init с PaymentURL — заказ ушёл на оплату.</summary>
    INIT_RESPONDED,

    /// <summary>
    /// Durable checkpoint перед server-side Charge. Если ответ Charge потерян, наличие
    /// события запрещает повторное списание даже когда GetState ещё возвращает NEW.
    /// </summary>
    CHARGE_CALLED,

    /// <summary>Init провалился (провайдер вернул ошибку / сеть / невалидный ответ). Заказ FAILED до редиректа.</summary>
    INIT_FAILED,

    /// <summary>
    /// Init вернул PaymentURL на недоверенном хосте (open-redirect guard, #440). Заказ FAILED,
    /// юзер НЕ редиректится. Отдельно от <see cref="INIT_FAILED"/> — это security-сигнал, а не
    /// обычный провайдерский отказ.
    /// </summary>
    INIT_URL_REJECTED,

    WEBHOOK_RECEIVED,
    WEBHOOK_REJECTED_INVALID_TOKEN,
    WEBHOOK_REJECTED_AMOUNT_MISMATCH,
    WEBHOOK_REJECTED_REPLAY,
    MARK_PAID,
    MARK_FAILED,

    /// <summary>PlanGrant (Source=PURCHASE) успешно выпущен после оплаты заказа.</summary>
    GRANT_ISSUED,

    /// <summary>
    /// Оплата прошла, но grant выпустить не удалось (план удалён до подтверждения и т.п.) —
    /// заказ FAILED, доступ требует ручной выдачи/возврата. Ключевой сигнал для админки.
    /// </summary>
    GRANT_FAILED,

    REFUNDED,

    /// <summary>
    /// T-Bank прислал <c>PARTIAL_REFUNDED</c>. Платформа частичные возвраты НЕ
    /// поддерживает — доступ сохраняется, заказ не меняет статус. Пишем audit-row
    /// (action=none), чтобы саппорт видел факт частичного refund'а при расследовании.
    /// </summary>
    PARTIAL_REFUND_IGNORED,
    RECONCILIATION_RECOVERED,
    RECONCILIATION_DEFERRED,
    REFUND_RECONCILIATION_CHECKED,
    MANUAL_GRANT_BY_ADMIN,
    MANUAL_REVOKE_BY_ADMIN,
}
