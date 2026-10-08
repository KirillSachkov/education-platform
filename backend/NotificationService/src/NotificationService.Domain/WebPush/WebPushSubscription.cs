using CSharpFunctionalExtensions;
using SharedKernel;

namespace NotificationService.Domain.WebPush;

/// <summary>
/// Web Push подписка устройства / Per-device Web Push subscription (RFC 8030 / RFC 8291).
///
/// Одна строка = один браузер/PWA-инстанс (push endpoint глобально уникален). Хранит
/// материал, необходимый для VAPID-доставки: <see cref="Endpoint"/> push-сервиса и две
/// клиентские ключевые величины — <see cref="P256dh"/> (публичный ключ устройства) и
/// <see cref="Auth"/> (auth secret) — для шифрования payload по RFC 8291. Issue #342.
///
/// Подписка per-device: у одного пользователя их может быть несколько (телефон + планшет).
/// Upsert и доставка работают по <see cref="Endpoint"/>; протухшие endpoint'ы (404/410 от
/// push-сервиса) удаляются каналом доставки.
/// </summary>
public sealed class WebPushSubscription
{
    private WebPushSubscription(
        WebPushSubscriptionId id,
        Guid userId,
        string endpoint,
        string p256dh,
        string auth,
        string? userAgent)
    {
        Id = id;
        UserId = userId;
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        UserAgent = userAgent;
        CreatedAt = DateTime.UtcNow;
        LastSeenAt = DateTime.UtcNow;
    }

    // EF Core
    private WebPushSubscription()
    {
    }

    /// <summary>Идентификатор подписки / Subscription identifier.</summary>
    public WebPushSubscriptionId Id { get; private set; } = null!;

    /// <summary>Идентификатор пользователя / User identifier.</summary>
    public Guid UserId { get; private set; }

    /// <summary>URL push-сервиса (глобально уникален) / Push service endpoint URL (globally unique).</summary>
    public string Endpoint { get; private set; } = null!;

    /// <summary>Публичный ключ устройства (base64url) / Device public key (base64url).</summary>
    public string P256dh { get; private set; } = null!;

    /// <summary>Auth secret устройства (base64url) / Device auth secret (base64url).</summary>
    public string Auth { get; private set; } = null!;

    /// <summary>User-Agent устройства (для диагностики) / Device user agent (diagnostics only).</summary>
    public string? UserAgent { get; private set; }

    /// <summary>Дата и время создания (UTC) / Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Последняя ре-регистрация (UTC) / Last re-registration timestamp (UTC).</summary>
    public DateTime LastSeenAt { get; private set; }

    /// <summary>
    /// Создаёт подписку с валидацией обязательных полей / Creates a subscription, validating required fields.
    /// </summary>
    public static Result<WebPushSubscription, Error> Create(
        Guid userId,
        string endpoint,
        string p256dh,
        string auth,
        string? userAgent)
    {
        if (userId == Guid.Empty)
            return GeneralErrors.ValueIsRequired("web_push.user");

        if (string.IsNullOrWhiteSpace(endpoint))
            return GeneralErrors.ValueIsRequired("web_push.endpoint");

        if (string.IsNullOrWhiteSpace(p256dh))
            return GeneralErrors.ValueIsRequired("web_push.p256dh");

        if (string.IsNullOrWhiteSpace(auth))
            return GeneralErrors.ValueIsRequired("web_push.auth");

        return new WebPushSubscription(
            WebPushSubscriptionId.Create(), userId, endpoint, p256dh, auth, userAgent);
    }
}
