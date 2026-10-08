namespace TelegramBotService.Core.Options;

/// <summary>
///     Настройки доставки уведомлений в Telegram / Telegram notification delivery options.
/// </summary>
public sealed class TelegramNotificationOptions
{
    public const string SECTION_NAME = "TelegramNotification";

    /// <summary>
    ///     Базовый URL фронтенда — для разворачивания относительных <c>deepLink</c>
    ///     в абсолютные ссылки внутри Telegram-сообщений / Frontend base URL used to expand
    ///     relative deep links into absolute URLs for Telegram messages.
    /// </summary>
    public string FrontendBaseUrl { get; init; } = string.Empty;
}
