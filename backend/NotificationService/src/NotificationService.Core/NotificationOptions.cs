namespace NotificationService.Core;

/// <summary>
/// Опции сервиса уведомлений / NotificationService options.
/// Биндятся из секции <c>Notifications</c> в appsettings.
/// </summary>
public sealed class NotificationOptions
{
    /// <summary>
    /// Базовый URL фронтенда — используется в email-шаблонах для абсолютных ссылок.
    /// Пример: <c>https://sachkov-learn.net</c> (prod), <c>http://localhost</c> (dev).
    /// </summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost";

    /// <summary>
    /// Ретенция inbox-уведомлений / Inbox retention settings.
    /// <c>NotificationRetentionService</c> в фоне удаляет строки старше порога.
    /// </summary>
    public NotificationRetentionOptions Retention { get; set; } = new();

    /// <summary>
    /// Настройки Unisender webhook'а для обработки bounce/spam/unsubscribe событий.
    /// </summary>
    public UnisenderWebhookOptions Unisender { get; set; } = new();

    /// <summary>
    /// Еженедельный дайджест «что нового за неделю» (#468).
    /// <c>WeeklyDigestService</c> в фоне раз в неделю шлёт по одному
    /// <c>NotificationType.WeeklyDigest</c> каждому получателю свежих
    /// MaterialPublished/IssuePublished-уведомлений.
    /// </summary>
    public NotificationDigestOptions Digest { get; set; } = new();
}

/// <summary>
/// Настройки еженедельного дайджеста (<c>Notifications:Digest</c>).
///
/// По умолчанию (как у <see cref="NotificationRetentionOptions"/> — включён во всех средах
/// кодовым дефолтом, без записей в appsettings; prod-parity с retention-сервисом):
/// <list type="bullet">
///   <item><see cref="Enabled"/> = <c>true</c></item>
///   <item><see cref="DayOfWeekUtc"/> = Monday, <see cref="HourUtc"/> = 7 — слот рассылки (UTC)</item>
///   <item><see cref="CheckIntervalMinutes"/> = 30 — частота проверки «пора ли»</item>
///   <item><see cref="InitialDelaySeconds"/> = 120 — отсрочка первого запуска после старта</item>
/// </list>
///
/// Watermark без миграции: последний глобальный запуск = <c>MAX(created_at)</c> существующих
/// уведомлений с <c>type = WeeklyDigest</c> (нет записей → ещё не запускался).
/// </summary>
public sealed class NotificationDigestOptions
{
    public bool Enabled { get; set; } = true;

    public DayOfWeek DayOfWeekUtc { get; set; } = DayOfWeek.Monday;

    public int HourUtc { get; set; } = 7;

    public int CheckIntervalMinutes { get; set; } = 30;

    public int InitialDelaySeconds { get; set; } = 120;
}

/// <summary>
/// <c>POST /webhooks/unisender</c> принимает статус-события. <see cref="WebhookSecret"/> —
/// shared secret для проверки через header <c>X-Unisender-Secret</c>. Если секрет не задан,
/// публичный мутирующий webhook работает fail-closed и отклоняет все запросы.
/// </summary>
public sealed class UnisenderWebhookOptions
{
    public string? WebhookSecret { get; set; }
}

/// <summary>
/// Настройки retention cleanup для таблиц <c>notifications</c> и <c>notification_deliveries</c>.
///
/// По умолчанию:
/// <list type="bullet">
///   <item><see cref="Enabled"/> = <c>true</c></item>
///   <item><see cref="DefaultDays"/> = 90 — старше = удаляется</item>
///   <item><see cref="IntervalHours"/> = 24 — раз в сутки</item>
///   <item><see cref="BatchSize"/> = 5000 — batch DELETE чтобы не держать долгий lock</item>
///   <item><see cref="InitialDelaySeconds"/> = 120 — отсрочка первого запуска после старта</item>
/// </list>
///
/// Удаление каскадное: сначала <c>notification_deliveries</c> (FK), потом <c>notifications</c>.
/// Subscriptions и user-preferences не трогаются — они persistent per-user.
/// </summary>
public sealed class NotificationRetentionOptions
{
    public bool Enabled { get; set; } = true;

    public int DefaultDays { get; set; } = 90;

    public int IntervalHours { get; set; } = 24;

    public int BatchSize { get; set; } = 5000;

    public int InitialDelaySeconds { get; set; } = 120;
}
