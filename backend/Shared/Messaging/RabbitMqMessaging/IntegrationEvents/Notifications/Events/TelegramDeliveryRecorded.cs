namespace Shared.Messaging.IntegrationEvents.Notifications.Events;

/// <summary>
/// Published by TelegramBotService after attempting to deliver a notification via Telegram Bot API.
/// Consumer: NotificationService — пишет результат в notification_deliveries (channel=Telegram).
/// Закрывает «слепое пятно» по доставке Telegram-уведомлений: до этого сервис публиковал
/// notification.created в TG и не имел никакого фидбека о судьбе сообщения.
/// </summary>
/// <param name="Status">
/// Один из <see cref="TelegramDeliveryStatuses"/>: <c>delivered</c> | <c>failed</c> | <c>skipped</c>.
/// </param>
/// <param name="ProviderMessageId">Telegram message_id при успехе; null при failed/skipped.</param>
/// <param name="ErrorCode">
/// Стабильный код ошибки для UI/метрик. См. <see cref="TelegramDeliveryErrorCodes"/>:
/// <c>no_user_link</c>, <c>bot_blocked</c>, <c>chat_not_found</c>, <c>flood_control</c>,
/// <c>api_error</c>, <c>empty_body</c>, <c>already_sent</c>. null при delivered.
/// </param>
/// <param name="ErrorDetail">Свободный текст для логов/диагностики.</param>
public sealed record TelegramDeliveryRecorded(
    Guid NotificationId,
    Guid RecipientUserId,
    long ChatId,
    string Status,
    string? ProviderMessageId,
    string? ErrorCode,
    string? ErrorDetail,
    DateTimeOffset RecordedAt);

/// <summary>
/// Стабильные строковые статусы для <see cref="TelegramDeliveryRecorded.Status"/>.
/// Меняются вместе с consumer'ом в NotificationService.
/// </summary>
public static class TelegramDeliveryStatuses
{
    public const string DELIVERED = "delivered";
    public const string FAILED = "failed";
    public const string SKIPPED = "skipped";
}

/// <summary>
/// Стабильные строковые коды ошибок Telegram-доставки. Используются в UI («бот заблокирован»),
/// в метриках (group by error_code) и для решения о soft-block UserLink.
/// </summary>
public static class TelegramDeliveryErrorCodes
{
    /// <summary>У получателя нет UserLink — Telegram не привязан или развязан.</summary>
    public const string NO_USER_LINK = "no_user_link";

    /// <summary>403 Forbidden: bot was blocked by the user.</summary>
    public const string BOT_BLOCKED = "bot_blocked";

    /// <summary>400 Bad Request: chat not found (удалённый аккаунт).</summary>
    public const string CHAT_NOT_FOUND = "chat_not_found";

    /// <summary>429 Too Many Requests, retry'и исчерпаны.</summary>
    public const string FLOOD_CONTROL = "flood_control";

    /// <summary>Прочая ошибка Telegram API (5xx, неклассифицированный 4xx).</summary>
    public const string API_ERROR = "api_error";

    /// <summary>Канал Telegram запросили, но TelegramBody пустой — баг на publisher-side.</summary>
    public const string EMPTY_BODY = "empty_body";

    /// <summary>Это сообщение уже было успешно отправлено (Redis idempotency hit). Status=skipped.</summary>
    public const string ALREADY_SENT = "already_sent";

    /// <summary>UserLink soft-blocked (бот раньше получил <c>bot_blocked</c>/<c>chat_not_found</c>
    /// и пометил link как заблокированный). Юзер вернётся через <c>/start</c> → link unblock'нётся
    /// автоматически, после чего следующее уведомление пойдёт нормально.</summary>
    public const string BLOCKED_LINK = "blocked_link";
}
