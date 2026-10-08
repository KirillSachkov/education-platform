using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBotService.Core.Delivery;

/// <summary>
/// Унифицированный entry-point для отправки уведомлений в Telegram. Поверх
/// <see cref="TelegramBotFlow.Core.Messaging.IBotNotifier"/> добавляет:
/// <list type="bullet">
/// <item>Idempotency через <see cref="ITelegramIdempotencyStore"/> — не шлёт повторно
/// если retry handler'а проскочил после успешной отправки.</item>
/// <item>Класcификацию ошибок Telegram API в <see cref="TelegramDeliveryOutcome"/>
/// со стабильными error_code'ами — handler'у не надо знать об <c>ApiRequestException</c>.</item>
/// <item>Auto-publish <c>notification.telegram_delivery_recorded</c> через durable outbox
/// для каждого outcome'а (delivered/skipped/failed). Closes слепое пятно по доставке.</item>
/// </list>
/// </summary>
public interface ITelegramDeliveryService
{
    /// <summary>
    /// Доставить уведомление пользователю. Никогда не throw'ит ApiRequestException —
    /// все ошибки классифицируются и возвращаются в <see cref="TelegramDeliveryOutcome"/>.
    /// Throw'ит только при unexpected exception (network до Polly, OOM и т.п.) — те
    /// уйдут в Wolverine retry / DLQ как обычно.
    /// </summary>
    Task<TelegramDeliveryOutcome> DeliverAsync(
        Guid notificationId,
        Guid recipientUserId,
        long chatId,
        string text,
        InlineKeyboardMarkup? keyboard,
        ParseMode parseMode,
        CancellationToken ct);

    /// <summary>
    /// Записать факт пропуска доставки без попытки отправки (no UserLink, empty body
    /// и т.п.). Публикует <c>notification.telegram_delivery_recorded</c> с
    /// <c>status=skipped</c> — чтобы UI/метрики знали, что уведомление по этому
    /// каналу не дошло.
    /// </summary>
    Task RecordSkipAsync(
        Guid notificationId,
        Guid recipientUserId,
        long chatId,
        string errorCode,
        string? errorDetail,
        CancellationToken ct);
}
