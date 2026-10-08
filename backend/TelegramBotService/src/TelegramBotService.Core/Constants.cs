namespace TelegramBotService.Core;

public static class TelegramBotConstants
{
    public const string DEFAULT_SCHEMA = "telegrambot";

    /// <summary>
    ///     Битовая маска канала <c>Telegram</c> в <c>NotificationChannel</c>.
    ///     NotificationService.Domain здесь не reference'им — дублируем значение.
    /// </summary>
    public const short TELEGRAM_CHANNEL_BIT = 2;
}
