using System.Diagnostics.Metrics;
using NotificationService.Domain.Notifications;
using Observability;

namespace NotificationService.Core.Diagnostics;

/// <summary>
/// Бизнес-метрики notification pipeline. Регистрируется как singleton, MeterFactory
/// инжектится по правилу из root CLAUDE.md ("точечно через IMeterFactory, не разводи
/// static helpers"). Имя meter'а — <see cref="ObservabilityExtensions.NOTIFICATIONS_METER"/>;
/// при изменении нужно синхронно поправить регистрацию в OTel pipeline.
///
/// Что покрываем:
/// <list type="bullet">
/// <item><b>dispatch_duration_seconds</b> — handler-side: время от прихода NotificationRequest
///     в <c>NotificationDispatcher.DispatchAsync</c> до завершения per-request обработки
///     (включает render + БД-запись + outbox publish). Помогает увидеть ботлнек на batch'ах.</item>
/// <item><b>outbox_publish_lag_seconds</b> — измеряется в consumer'ах <c>NotificationCreated</c>
///     как <c>now - evt.CreatedAt</c>. Это **сквозная** задержка от выпуска domain-нотификации
///     до её обработки на consumer-стороне (SSE fan-out, Telegram delivery). Большие значения
///     = stuck outgoing envelope (issue #20 регрессия).</item>
/// <item><b>delivery_outcomes_total</b> — counter per (channel, status, error_code).
///     Заменяет руками-собранный SQL по <c>notification_deliveries</c> для health-check'а.</item>
/// </list>
/// </summary>
public sealed class NotificationMetrics
{
    /// <summary>
    /// Histogram boundaries в секундах. Сетка плотнее в области &lt;1s где живут нормальные
    /// dispatch'и; выше — для catch'а stuck/recovery sweep случаев (порядка 60-120s).
    /// </summary>
    public static readonly double[] LATENCY_BUCKETS_SECONDS =
        [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300];

    private readonly Histogram<double> _dispatchDuration;
    private readonly Histogram<double> _outboxPublishLag;
    private readonly Counter<long> _deliveryOutcomes;

    public NotificationMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(ObservabilityExtensions.NOTIFICATIONS_METER);

        _dispatchDuration = meter.CreateHistogram<double>(
            name: "notification_dispatch_duration_seconds",
            unit: "s",
            description: "Time spent dispatching one NotificationRequest (render + DB write + outbox publish).");

        _outboxPublishLag = meter.CreateHistogram<double>(
            name: "notification_outbox_publish_lag_seconds",
            unit: "s",
            description: "Lag between Notification.CreatedAt and integration-event consumer ack time.");

        _deliveryOutcomes = meter.CreateCounter<long>(
            name: "notification_delivery_outcomes_total",
            unit: "{outcome}",
            description: "Per-channel delivery results recorded into notification_deliveries.");
    }

    public void RecordDispatch(NotificationType type, TimeSpan elapsed, string status)
    {
        _dispatchDuration.Record(
            elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("notification_type", type.ToString()),
            new KeyValuePair<string, object?>("status", status));
    }

    public void RecordOutboxLag(string consumer, NotificationType type, TimeSpan lag)
    {
        // Отрицательные значения в кейсе clock skew — обрезаем до 0, иначе histogram бьётся.
        double lagSeconds = lag.TotalSeconds < 0 ? 0 : lag.TotalSeconds;
        _outboxPublishLag.Record(
            lagSeconds,
            new KeyValuePair<string, object?>("consumer", consumer),
            new KeyValuePair<string, object?>("notification_type", type.ToString()));
    }

    public void RecordDelivery(NotificationChannel channel, string status, string? errorCode)
    {
        _deliveryOutcomes.Add(
            1,
            new KeyValuePair<string, object?>("channel", channel.ToString()),
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>("error_code", errorCode ?? string.Empty));
    }
}
