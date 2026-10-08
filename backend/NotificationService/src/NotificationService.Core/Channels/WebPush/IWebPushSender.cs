namespace NotificationService.Core.Channels.WebPush;

/// <summary>
/// Результат попытки доставки одного push'а / Outcome of a single push delivery attempt.
/// </summary>
public enum WebPushSendOutcome
{
    /// <summary>Push принят push-сервисом (201/200/202).</summary>
    Delivered,

    /// <summary>Подписка протухла (404/410) — нужно удалить из БД.</summary>
    Gone,

    /// <summary>Прочая ошибка доставки — оставляем подписку, логируем.</summary>
    Failed,
}

/// <summary>
/// Транспорт Web Push / Web Push transport. Абстракция над библиотекой VAPID-доставки —
/// позволяет подменять реальную отправку фейком в тестах. Issue #342.
/// </summary>
public interface IWebPushSender
{
    Task<WebPushSendOutcome> SendAsync(
        string endpoint,
        string p256dh,
        string auth,
        string payloadJson,
        CancellationToken cancellationToken = default);
}
