namespace AccessService.Core.Features.Billing;

/// <summary>
/// Нормализует сырой <c>Order.FailureReason</c> (содержит T-Bank errorCode'ы, суммы,
/// свободно-форматные строки вроде <c>amount_mismatch: webhook=..., order=...</c>) в:
/// <list type="bullet">
///   <item><see cref="ToMetricBucket"/> — bounded-cardinality label для Prometheus
///     (<c>payments_order_failed_total</c>). Без него label cardinality взорвётся.</item>
///   <item><see cref="ToUserFacing"/> — короткое русское сообщение для UI
///     (<c>GET /access/orders/{id}/status</c>). Сырой reason НЕ утекает наружу: он
///     содержит внутренние суммы / errorCode'ы и остаётся только в логах и
///     <c>order_events.payload</c>.</item>
/// </list>
/// Single source of truth — оба webhook-handler и order-status endpoint бьют один bucket.
/// </summary>
public static class FailureReasonNormalizer
{
    /// <summary>
    /// Стабильный машинный bucket (для метрик). Cardinality ограничена фиксированным
    /// набором значений ниже + <c>other</c> / <c>unspecified</c>.
    /// </summary>
    public static string ToMetricBucket(string? reason)
    {
        if (string.IsNullOrEmpty(reason)) return "unspecified";
        if (reason.StartsWith("rejected", StringComparison.Ordinal)) return "rejected";
        if (reason.StartsWith("amount_mismatch", StringComparison.Ordinal)) return "amount_mismatch";
        if (string.Equals(reason, "reversed", StringComparison.Ordinal)) return "reversed";
        if (string.Equals(reason, "deadline_expired", StringComparison.Ordinal)) return "deadline_expired";
        if (string.Equals(reason, "attempts_expired", StringComparison.Ordinal)) return "attempts_expired";
        if (string.Equals(reason, "canceled", StringComparison.Ordinal)) return "canceled";
        if (string.Equals(reason, "reconciliation_expired", StringComparison.Ordinal)) return "reconciliation_expired";
        return "other";
    }

    /// <summary>
    /// Русское пользовательское сообщение, безопасное к показу в UI. Маппит сырой reason
    /// через <see cref="ToMetricBucket"/> и возвращает заранее заданный текст; внутренние
    /// детали (суммы, errorCode'ы) не раскрываются. <c>null</c> reason → <c>null</c>
    /// (заказ ещё не падал — endpoint вернёт null, фронт ничего не покажет).
    /// </summary>
    public static string? ToUserFacing(string? reason)
    {
        if (string.IsNullOrEmpty(reason)) return null;

        return ToMetricBucket(reason) switch
        {
            "rejected" => "Платёж отклонён банком",
            "amount_mismatch" => "Сумма платежа не совпала с заказом",
            "reversed" => "Платёж отменён",
            "deadline_expired" => "Истёк срок оплаты",
            "attempts_expired" => "Исчерпаны попытки оплаты",
            "canceled" => "Платёж отменён",
            "reconciliation_expired" => "Истёк срок ожидания оплаты",
            _ => "Не удалось завершить оплату",
        };
    }
}
