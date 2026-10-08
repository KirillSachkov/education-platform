using CSharpFunctionalExtensions;
using SharedKernel;

namespace NotificationService.Domain.UserChannels;

/// <summary>
/// Per-user предпочтения каналов доставки / Per-user delivery channel preferences.
///
/// Упрощённая модель (заменила <c>NotificationPreference(user, type) → bitmask</c>):
/// одна запись на пользователя, два флажка — Telegram и Email. Сайт (InApp) приходит всегда
/// — это продуктовое решение, не настраивается, чтобы пользователь не потерял важные письма
/// случайным кликом.
///
/// Если записи нет — user получает дефолты: InApp ON, Telegram ON, Email ON. При
/// регистрации <c>UserCreated</c>-handler создаёт дефолтную запись; Telegram-доставка без
/// локального <c>UserLink</c> будет пропущена TelegramBotService, поэтому канал безопасно
/// включать заранее.
/// </summary>
public sealed class UserNotificationChannels
{
    private UserNotificationChannels(Guid userId, bool telegramEnabled, bool emailEnabled, bool webPushEnabled)
    {
        UserId = userId;
        TelegramEnabled = telegramEnabled;
        EmailEnabled = emailEnabled;
        WebPushEnabled = webPushEnabled;
        UpdatedAt = DateTime.UtcNow;
    }

    // EF Core
    private UserNotificationChannels()
    {
    }

    /// <summary>Идентификатор пользователя (PK) / User identifier (PK).</summary>
    public Guid UserId { get; private set; }

    /// <summary>Telegram-уведомления включены / Telegram notifications enabled.</summary>
    public bool TelegramEnabled { get; private set; }

    /// <summary>Email-уведомления включены / Email notifications enabled.</summary>
    public bool EmailEnabled { get; private set; }

    /// <summary>
    /// Web Push (браузер / PWA) уведомления включены / Web Push notifications enabled.
    /// Глобальный тумблер: выключение не доставляет push ни по одному типу, даже при наличии
    /// активной подписки устройства. Дефолт ON — push идёт сразу после регистрации устройства
    /// (без активной подписки канал просто пропускается). Issue #342.
    /// </summary>
    public bool WebPushEnabled { get; private set; }

    /// <summary>Дата и время последнего изменения (UTC) / Last update (UTC).</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Создаёт запись с переданными флагами / Creates a new row with the given flags.
    /// </summary>
    public static Result<UserNotificationChannels, Error> Create(
        Guid userId,
        bool telegramEnabled,
        bool emailEnabled,
        bool webPushEnabled = true)
    {
        if (userId == Guid.Empty)
            return GeneralErrors.ValueIsRequired("user_channels.user");

        return new UserNotificationChannels(userId, telegramEnabled, emailEnabled, webPushEnabled);
    }

    /// <summary>
    /// Дефолтные значения для нового пользователя / Default flags for a freshly-registered user.
    /// Оба канала ON: Email — чтобы welcome-email и transactional письма приходили из коробки;
    /// Telegram — чтобы после привязки бота уведомления шли сразу, без ручного включения toggle.
    /// До привязки `UserLink`-а Telegram-доставка просто пропускается на стороне TelegramBotService —
    /// флаг безопасно держать включённым «впрок».
    /// </summary>
    public static UserNotificationChannels Default(Guid userId) =>
        new(userId, telegramEnabled: true, emailEnabled: true, webPushEnabled: true);

    /// <summary>
    /// Обновляет флаги / Updates the flags.
    /// </summary>
    public void Update(bool telegramEnabled, bool emailEnabled)
    {
        TelegramEnabled = telegramEnabled;
        EmailEnabled = emailEnabled;
        UpdatedAt = DateTime.UtcNow;
    }

    public void EnableTelegram()
    {
        if (TelegramEnabled)
            return;
        TelegramEnabled = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DisableTelegram()
    {
        if (!TelegramEnabled)
            return;
        TelegramEnabled = false;
        UpdatedAt = DateTime.UtcNow;
    }
}
