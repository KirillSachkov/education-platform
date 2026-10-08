namespace NotificationService.Contracts.WebPush.Requests;

/// <summary>
/// Отписка устройства от web-push / Remove a device's web-push subscription by endpoint.
/// </summary>
public sealed record RemoveWebPushSubscriptionRequest
{
    public required string Endpoint { get; init; }
}
