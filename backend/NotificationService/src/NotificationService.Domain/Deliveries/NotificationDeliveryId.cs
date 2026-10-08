namespace NotificationService.Domain.Deliveries;

/// <summary>
/// Идентификатор записи о доставке уведомления / Notification delivery identifier.
/// </summary>
public sealed record NotificationDeliveryId
{
    private NotificationDeliveryId(Guid value) => Value = value;

    /// <summary>
    /// Значение идентификатора / Identifier value.
    /// </summary>
    public Guid Value { get; private set; }

    /// <summary>
    /// Создаёт новый идентификатор доставки / Creates a new delivery identifier.
    /// </summary>
    public static NotificationDeliveryId Create() => new(Guid.CreateVersion7());

    /// <summary>
    /// Создаёт идентификатор доставки из GUID / Creates a delivery identifier from GUID.
    /// </summary>
    public static NotificationDeliveryId Of(Guid id) => new(id);

    /// <summary>
    /// Создаёт массив идентификаторов доставок из массива GUID / Creates an array of delivery identifiers from GUID array.
    /// </summary>
    public static NotificationDeliveryId[] Of(IEnumerable<Guid> ids) => [.. ids.Select(Of)];
}
