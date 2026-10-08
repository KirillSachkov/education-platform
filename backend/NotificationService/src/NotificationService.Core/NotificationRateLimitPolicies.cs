namespace NotificationService.Core;

/// <summary>
/// Rate-limit policy names. Константы вынесены в Core чтобы endpoint'ы (которые тоже в Core)
/// могли ссылаться на них без зависимости от Web-слоя. Реализация policies —
/// <c>NotificationService.Web.Configuration.NotificationRateLimiting</c>.
/// </summary>
public static class NotificationRateLimitPolicies
{
    /// <summary>
    /// <c>GET /notifications/stream</c> — concurrency-limiter по userId (default 3 одновременных
    /// соединений). Защита от leak'а и script-абуза.
    /// </summary>
    public const string STREAM = "notifications.stream";

    /// <summary>
    /// <c>PUT /notifications/preferences/</c> — fixed-window по userId (default 10/min).
    /// </summary>
    public const string PREFERENCES = "notifications.preferences.update";

    /// <summary>
    /// <c>POST /notifications/broadcast/</c> — fixed-window по userId (default 5/60min).
    /// Агрессивное ограничение: broadcast = fan-out на N подписчиков.
    /// </summary>
    public const string BROADCAST = "notifications.broadcast";

    /// <summary>
    /// <c>POST/DELETE /notifications/push/subscriptions</c> — fixed-window по userId (30/min).
    /// Защита от спама регистрации устройств (#342); вместе с per-user device cap бьёт
    /// amplification-вектор доставки.
    /// </summary>
    public const string PUSH_REGISTER = "notifications.push.register";

    /// <summary>
    /// <c>GET /n/{id}</c> — fixed-window по userId/IP (30/min).
    /// Публичный click-through делает indexed DB lookup даже для неизвестного id.
    /// </summary>
    public const string OPEN = "notifications.open";
}
