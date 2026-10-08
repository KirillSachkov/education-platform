using NotificationService.Domain.Deliveries;
using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Database;

public interface IDeliveriesRepository
{
    Task AddAsync(NotificationDelivery delivery, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает первую попытку доставки уведомления по каналу или <c>null</c>.
    /// Используется handler'ом <c>TelegramDeliveryRecorded</c> для идемпотентного
    /// upsert: если запись уже есть (Wolverine retry того же event'a) — обновляем,
    /// иначе создаём.
    /// </summary>
    Task<NotificationDelivery?> GetForNotificationAsync(
        NotificationId notificationId,
        NotificationChannel channel,
        CancellationToken cancellationToken = default);
}
