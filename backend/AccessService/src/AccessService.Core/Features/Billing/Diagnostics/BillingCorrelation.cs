using System.Diagnostics;

namespace AccessService.Core.Features.Billing.Diagnostics;

/// <summary>
/// Единый источник correlation id для billing-аудита (#443): OTel trace_id текущего
/// <see cref="Activity"/>. Связывает <c>orders.correlation_id</c> / <c>order_events.correlation_id</c>
/// с логами (trace_id) и трейсами (Tempo) — чтобы расследование «оплатил, но доступа нет»
/// шло от заказа в админке прямо в трейс. Null, если активного <see cref="Activity"/> нет
/// (часть фоновых reconciliation-tick'ов).
/// </summary>
internal static class BillingCorrelation
{
    public static string? CurrentTraceId() => Activity.Current?.TraceId.ToString();
}
