using System.Diagnostics.Metrics;
using Observability;

namespace TelegramBotService.Core.Diagnostics;

/// <summary>
/// Метрики Telegram-доставки. Singleton + IMeterFactory (правило root CLAUDE.md).
/// Имя meter — <see cref="ObservabilityExtensions.TELEGRAM_METER"/>; должно быть зарегистрировано
/// в OTel pipeline (см. <c>ObservabilityExtensions.AddObservability</c>).
///
/// Покрываем три критичные точки в delivery pipeline:
/// <list type="bullet">
/// <item><b>send_duration_seconds</b> — реальное время <c>SendTextAsync</c> к Telegram API
///     (NOT включая throttle wait). p95/p99 — баромметр здоровья TG API + сети.</item>
/// <item><b>throttle_wait_seconds</b> — сколько <c>PerChatBotThrottler</c> подержал send
///     перед отправкой (1s/chat ограничение Telegram). Если p95 высокий — значит fan-out
///     мощнее чем listener concurrency может прожевать без накопления очереди.</item>
/// <item><b>delivery_outcomes_total</b> — counter per (outcome, error_code). Заменяет ручной
///     SQL по <c>notification_deliveries</c> для Telegram-канала.</item>
/// <item><b>handler_lag_seconds</b> — время от <c>NotificationCreated.CreatedAt</c> до начала
///     обработки в TG-боте. Большие значения = очередь забилась или single listener.</item>
/// </list>
/// </summary>
public sealed class TelegramMetrics
{
    /// <summary>
    /// Telegram API обычно отвечает за 50-300ms; throttle wait — 0-1000ms; handler lag
    /// мы хотим видеть и в нанометровом, и в "час застрял" разрешении.
    /// </summary>
    public static readonly double[] LATENCY_BUCKETS_SECONDS =
        [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300];

    private readonly Histogram<double> _sendDuration;
    private readonly Histogram<double> _throttleWait;
    private readonly Histogram<double> _handlerLag;
    private readonly Counter<long> _outcomes;

    public TelegramMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(ObservabilityExtensions.TELEGRAM_METER);

        _sendDuration = meter.CreateHistogram<double>(
            name: "telegram_send_duration_seconds",
            unit: "s",
            description: "Time spent in IBotNotifier.SendTextAsync (Telegram Bot API call).");

        _throttleWait = meter.CreateHistogram<double>(
            name: "telegram_throttle_wait_seconds",
            unit: "s",
            description: "Wait time inside PerChatBotThrottler before send (per-chat 1s gate).");

        _handlerLag = meter.CreateHistogram<double>(
            name: "telegram_handler_lag_seconds",
            unit: "s",
            description: "Lag from NotificationCreated.CreatedAt to handler start (queue + listener).");

        _outcomes = meter.CreateCounter<long>(
            name: "telegram_delivery_outcomes_total",
            unit: "{outcome}",
            description: "Telegram delivery outcomes per (outcome_kind, error_code).");
    }

    public void RecordSend(TimeSpan elapsed, string outcome, string? errorCode)
    {
        _sendDuration.Record(
            elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("error_code", errorCode ?? string.Empty));
    }

    public void RecordThrottleWait(TimeSpan wait)
    {
        double seconds = wait.TotalSeconds < 0 ? 0 : wait.TotalSeconds;
        _throttleWait.Record(seconds);
    }

    public void RecordHandlerLag(TimeSpan lag)
    {
        double seconds = lag.TotalSeconds < 0 ? 0 : lag.TotalSeconds;
        _handlerLag.Record(seconds);
    }

    public void RecordOutcome(string outcome, string? errorCode)
    {
        _outcomes.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("error_code", errorCode ?? string.Empty));
    }
}
