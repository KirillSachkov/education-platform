namespace AccessService.Core.Features.Billing.Reconciliation;

/// <summary>
/// Конфиг для <see cref="PendingOrderReconciliationService"/>. Все параметры опциональны
/// (есть defaults). Привязка к секции <c>PaymentsReconciliation:</c> через <see cref="SECTION_NAME"/>.
/// </summary>
public sealed class ReconciliationOptions
{
    public const string SECTION_NAME = "PaymentsReconciliation";

    /// <summary>Интервал между tick'ами BackgroundService. Default 30 сек.</summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>Order не реконсилируется, пока не достиг этого возраста. Default 2 минуты —
    /// чтобы не гнать GetState'ы сразу после Init (T-Bank сам обработает за пару секунд).</summary>
    public int MinAgeSecondsBeforePoll { get; set; } = 120;

    /// <summary>Order, провисевший в PENDING больше этого срока, помечается FAILED("expired") —
    /// alert + терминальное состояние. Default 24h.</summary>
    public int MaxPendingHoursBeforeExpire { get; set; } = 24;

    /// <summary>Сколько orders обрабатывать за один tick (защита от взрыва GetState'ов
    /// при катастрофическом backlog'е). Default 50.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Сколько секунд не выбирать заказ повторно после неавторитетного/неоднозначного
    /// ответа провайдера. Не даёт старым poison rows занимать весь bounded batch.
    /// </summary>
    public int RetryDeferralSeconds { get; set; } = 300;

    /// <summary>Cooldown между GetState-проверками уже PAID renewal-заказов.</summary>
    public int RefundPollIntervalSeconds { get; set; } = 86400;

    /// <summary>Глубина проверки потерянных refund-webhook'ов.</summary>
    public int RefundLookbackDays { get; set; } = 180;

    /// <summary>TTL для записей в <c>idempotency_keys</c>. Каждый tick подчищаем
    /// старые записи. Default 24h — соответствует <see cref="MaxPendingHoursBeforeExpire"/>:
    /// если Order PENDING более 24h — он уже expired, повторный CreateOrder с тем
    /// же ключом смысла не имеет.</summary>
    public int IdempotencyKeyTtlHours { get; set; } = 24;
}
