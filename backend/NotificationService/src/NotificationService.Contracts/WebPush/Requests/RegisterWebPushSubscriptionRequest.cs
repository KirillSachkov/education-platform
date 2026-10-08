namespace NotificationService.Contracts.WebPush.Requests;

/// <summary>
/// Регистрация web-push подписки устройства / Register a device's web-push subscription.
/// Поля приходят из <c>PushSubscription</c> браузера (issue #342):
/// <c>endpoint</c> + ключи <c>p256dh</c>/<c>auth</c> из <c>getKey()</c> (base64url).
/// </summary>
public sealed record RegisterWebPushSubscriptionRequest
{
    public required string Endpoint { get; init; }

    public required string P256dh { get; init; }

    public required string Auth { get; init; }

    /// <summary>User-Agent устройства (диагностика) / Device user agent (diagnostics only).</summary>
    public string? UserAgent { get; init; }
}
