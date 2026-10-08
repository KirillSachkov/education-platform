using NotificationService.Core.Templates;
using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Channels;

/// <summary>
/// Стратегия доставки уведомления через конкретный канал (InApp / Telegram / Email).
/// Реализации регистрируются в DI как <c>INotificationChannel</c>, Dispatcher выбирает их по
/// <see cref="Type"/>. Новый канал = новая реализация, правила диспатча не меняются.
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// Канал, за который отвечает реализация. Должен быть «единичным» флагом (не комбинация).
    /// </summary>
    NotificationChannel Type { get; }

    Task<DeliveryResult> SendAsync(
        Notification notification,
        RenderedMessage message,
        CancellationToken cancellationToken = default);
}

public sealed record DeliveryResult(
    bool IsSuccess,
    bool IsSkipped,
    string? ProviderMessageId,
    string? ErrorCode,
    string? ErrorDetail)
{
    public static DeliveryResult Success(string? providerMessageId = null) =>
        new(IsSuccess: true, IsSkipped: false, providerMessageId, null, null);

    public static DeliveryResult Failed(string errorCode, string errorDetail) =>
        new(IsSuccess: false, IsSkipped: false, null, errorCode, errorDetail);

    /// <summary>
    /// Доставка пропущена осознанно (нет email, юзер opted-out, no Telegram link и т.п.).
    /// <c>errorCode</c> — стабильный код причины (например <c>user.email.not_found</c>),
    /// попадает в <c>notification_deliveries.error_code</c> для последующей группировки
    /// в метриках и алертах. Раньше использовался sentinel <c>"skipped"</c> в error_code,
    /// и причина уходила в error_detail — `GROUP BY error_code` для skipped-записей не
    /// работал.
    /// </summary>
    public static DeliveryResult Skipped(string errorCode, string? errorDetail = null) =>
        new(IsSuccess: false, IsSkipped: true, null, errorCode, errorDetail);
}
