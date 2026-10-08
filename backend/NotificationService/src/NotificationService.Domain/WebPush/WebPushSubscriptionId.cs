namespace NotificationService.Domain.WebPush;

/// <summary>
/// Идентификатор web-push подписки / Web-push subscription identifier.
/// </summary>
public sealed record WebPushSubscriptionId
{
    private WebPushSubscriptionId(Guid value) => Value = value;

    /// <summary>Значение идентификатора / Identifier value.</summary>
    public Guid Value { get; private set; }

    /// <summary>Создаёт новый идентификатор / Creates a new identifier.</summary>
    public static WebPushSubscriptionId Create() => new(Guid.CreateVersion7());

    /// <summary>Создаёт идентификатор из GUID / Creates an identifier from GUID.</summary>
    public static WebPushSubscriptionId Of(Guid id) => new(id);
}
