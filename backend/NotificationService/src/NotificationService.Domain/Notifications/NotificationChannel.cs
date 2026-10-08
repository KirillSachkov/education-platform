namespace NotificationService.Domain.Notifications;

/// <summary>
/// Канал доставки уведомления (битовая маска) / Notification delivery channel (bitmask).
/// </summary>
[Flags]
public enum NotificationChannel
{
    None = 0,
    InApp = 1,
    Telegram = 2,
    Email = 4,

    /// <summary>
    /// Web Push (браузер / установленный PWA) через VAPID + RFC 8291 payload encryption.
    /// Доставляется синхронно внутри NotificationService (как Email), переиспользует InApp
    /// Title/Body — отдельного template-part не требует. Issue #342.
    /// </summary>
    WebPush = 8,
}
