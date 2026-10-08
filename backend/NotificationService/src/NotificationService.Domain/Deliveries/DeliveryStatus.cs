namespace NotificationService.Domain.Deliveries;

/// <summary>
/// Статус доставки уведомления / Notification delivery status.
/// </summary>
public enum DeliveryStatus
{
    Pending = 0,
    Delivered = 1,
    Failed = 2,
    Skipped = 3,
}
