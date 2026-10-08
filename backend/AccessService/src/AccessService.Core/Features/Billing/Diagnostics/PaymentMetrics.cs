using System.Diagnostics.Metrics;

namespace AccessService.Core.Features.Billing.Diagnostics;

/// <summary>
/// Бизнес-метрики для платёжного pipeline. Phase F.1.0 — skeleton; реальные
/// приращения добавляются в use-cases F.1.1+. Meter <c>EducationPlatform.Payments</c>
/// регистрируется в <c>ObservabilityExtensions</c> — без регистрации OTel молча
/// дропает meter.
/// </summary>
public sealed class PaymentMetrics
{
    public const string MeterName = "EducationPlatform.Payments";

    private readonly Counter<long> _orderCreated;
    private readonly Counter<long> _orderInitOutcomes;
    private readonly Counter<long> _webhookReceived;
    private readonly Counter<long> _orderPaid;
    private readonly Counter<long> _orderFailed;
    private readonly Counter<long> _reconciliationRecovered;
    private readonly Counter<long> _chargeOutcomes;
    private readonly Counter<long> _checkOrderRecoveryOutcomes;
    private readonly Counter<long> _rebillRecoveryOutcomes;
    private readonly Histogram<double> _orderInitDuration;
    private readonly Histogram<double> _orderE2eDuration;
    private readonly Histogram<double> _chargeDuration;

    public PaymentMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);

        _orderCreated = meter.CreateCounter<long>(
            "payments_order_created_total",
            description: "Total orders created (POST /access/orders).");

        _orderInitOutcomes = meter.CreateCounter<long>(
            "payments_order_init_outcomes_total",
            description: "Provider Init outcomes (ok | provider_error | timeout).");

        _webhookReceived = meter.CreateCounter<long>(
            "payments_webhook_received_total",
            description: "Webhook receptions by outcome (ok | invalid_token | order_not_found | amount_mismatch | state_violation).");

        _orderPaid = meter.CreateCounter<long>(
            "payments_order_paid_total",
            description: "Orders successfully marked PAID.");

        _orderFailed = meter.CreateCounter<long>(
            "payments_order_failed_total",
            description: "Orders marked FAILED.");

        _reconciliationRecovered = meter.CreateCounter<long>(
            "payments_reconciliation_recovered_total",
            description: "Orders recovered by reconciliation BackgroundService.");

        _orderInitDuration = meter.CreateHistogram<double>(
            "payments_order_init_duration_seconds",
            unit: "s",
            description: "Provider Init API call latency.");

        _orderE2eDuration = meter.CreateHistogram<double>(
            "payments_order_e2e_duration_seconds",
            unit: "s",
            description: "End-to-end Order CreatedAt → PaidAt.");

        _chargeOutcomes = meter.CreateCounter<long>(
            "payments_charge_outcomes_total",
            description: "Recurring Charge outcomes (ok | provider_error | timeout | network_error | http_error | invalid_response).");

        _checkOrderRecoveryOutcomes = meter.CreateCounter<long>(
            "payments_check_order_recovery_outcomes_total",
            description: "CheckOrder recovery outcomes for durable orders missing PaymentId.");

        _rebillRecoveryOutcomes = meter.CreateCounter<long>(
            "payments_rebill_recovery_outcomes_total",
            description: "GetCardList RebillId recovery outcomes for subscription orders.");

        _chargeDuration = meter.CreateHistogram<double>(
            "payments_charge_duration_seconds",
            unit: "s",
            description: "Provider Charge API call latency (recurring auto-renew).");
    }

    public void RecordOrderCreated(string provider, string planKind) =>
        _orderCreated.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("plan_kind", planKind));

    public void RecordInitOutcome(string provider, string outcome) =>
        _orderInitOutcomes.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordWebhookReceived(string provider, string outcome) =>
        _webhookReceived.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordOrderPaid(string provider, string planKind) =>
        _orderPaid.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("plan_kind", planKind));

    public void RecordOrderFailed(string provider, string reason) =>
        _orderFailed.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("reason", reason));

    public void RecordReconciliationRecovered(string provider, string fromStatus) =>
        _reconciliationRecovered.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("from_status", fromStatus));

    public void RecordInitDuration(string provider, double seconds) =>
        _orderInitDuration.Record(seconds, new KeyValuePair<string, object?>("provider", provider));

    public void RecordE2eDuration(string provider, string planKind, double seconds) =>
        _orderE2eDuration.Record(seconds, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("plan_kind", planKind));

    public void RecordChargeOutcome(string provider, string outcome) =>
        _chargeOutcomes.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordChargeDuration(string provider, double seconds) =>
        _chargeDuration.Record(seconds, new KeyValuePair<string, object?>("provider", provider));

    public void RecordCheckOrderRecovery(string provider, string outcome) =>
        _checkOrderRecoveryOutcomes.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordRebillRecovery(string provider, string outcome) =>
        _rebillRecoveryOutcomes.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("outcome", outcome));
}
