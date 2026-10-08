namespace NotificationService.Domain.Subscriptions;

/// <summary>
/// Идентификатор подписки / Subscription identifier.
/// </summary>
public sealed record SubscriptionId
{
    private SubscriptionId(Guid value) => Value = value;

    /// <summary>
    /// Значение идентификатора / Identifier value.
    /// </summary>
    public Guid Value { get; private set; }

    /// <summary>
    /// Создаёт новый идентификатор подписки / Creates a new subscription identifier.
    /// </summary>
    public static SubscriptionId Create() => new(Guid.CreateVersion7());

    /// <summary>
    /// Создаёт идентификатор подписки из GUID / Creates a subscription identifier from GUID.
    /// </summary>
    public static SubscriptionId Of(Guid id) => new(id);

    /// <summary>
    /// Создаёт массив идентификаторов подписок из массива GUID / Creates an array of subscription identifiers from GUID array.
    /// </summary>
    public static SubscriptionId[] Of(IEnumerable<Guid> ids) => [.. ids.Select(Of)];
}
