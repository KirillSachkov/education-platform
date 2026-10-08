using Shared.Messaging.IntegrationEvents.Notifications.Events;

namespace TelegramBotService.Core.Delivery;

/// <summary>
/// Результат попытки доставки в Telegram. Driver двух действий:
/// <list type="bullet">
/// <item>Публикация <see cref="TelegramDeliveryRecorded"/> со status/error_code.</item>
/// <item>Решение handler'а: удалить ли UserLink (для bot_blocked / chat_not_found),
/// проглотить ли ошибку (flood_control), просто залогировать (api_error).</item>
/// </list>
/// </summary>
public readonly record struct TelegramDeliveryOutcome
{
    private TelegramDeliveryOutcome(
        TelegramDeliveryOutcomeKind kind,
        int? providerMessageId,
        string? errorCode,
        string? errorDetail)
    {
        Kind = kind;
        ProviderMessageId = providerMessageId;
        ErrorCode = errorCode;
        ErrorDetail = errorDetail;
    }

    public TelegramDeliveryOutcomeKind Kind { get; }
    public int? ProviderMessageId { get; }
    public string? ErrorCode { get; }
    public string? ErrorDetail { get; }

    /// <summary>Telegram message отправлен (или уже был отправлен — idempotency hit).</summary>
    public bool IsDelivered => Kind == TelegramDeliveryOutcomeKind.Delivered;

    /// <summary>Skipped — без попытки отправки (no UserLink / empty body / already sent).</summary>
    public bool IsSkipped => Kind == TelegramDeliveryOutcomeKind.Skipped;

    /// <summary>Telegram API вернул ошибку, не получилось отправить.</summary>
    public bool IsFailed => Kind == TelegramDeliveryOutcomeKind.Failed;

    public static TelegramDeliveryOutcome Delivered(int providerMessageId) =>
        new(TelegramDeliveryOutcomeKind.Delivered, providerMessageId, errorCode: null, errorDetail: null);

    public static TelegramDeliveryOutcome AlreadySent(int providerMessageId) =>
        new(TelegramDeliveryOutcomeKind.Skipped, providerMessageId,
            TelegramDeliveryErrorCodes.ALREADY_SENT, errorDetail: null);

    public static TelegramDeliveryOutcome Skipped(string errorCode, string? errorDetail = null) =>
        new(TelegramDeliveryOutcomeKind.Skipped, providerMessageId: null, errorCode, errorDetail);

    public static TelegramDeliveryOutcome Failed(string errorCode, string? errorDetail = null) =>
        new(TelegramDeliveryOutcomeKind.Failed, providerMessageId: null, errorCode, errorDetail);
}

public enum TelegramDeliveryOutcomeKind
{
    Delivered,
    Skipped,
    Failed
}
