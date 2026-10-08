namespace NotificationService.Domain.Notifications;

/// <summary>
/// Идентификатор уведомления / Notification identifier.
/// </summary>
public sealed record NotificationId
{
    private NotificationId(Guid value) => Value = value;

    /// <summary>
    /// Значение идентификатора / Identifier value.
    /// </summary>
    public Guid Value { get; private set; }

    /// <summary>
    /// Создаёт новый идентификатор уведомления / Creates a new notification identifier.
    /// </summary>
    public static NotificationId Create() => new(Guid.CreateVersion7());

    /// <summary>
    /// Создаёт идентификатор уведомления из GUID / Creates a notification identifier from GUID.
    /// </summary>
    public static NotificationId Of(Guid id) => new(id);

    /// <summary>
    /// Создаёт массив идентификаторов уведомлений из массива GUID / Creates an array of notification identifiers from GUID array.
    /// </summary>
    public static NotificationId[] Of(IEnumerable<Guid> ids) => [.. ids.Select(Of)];
}
