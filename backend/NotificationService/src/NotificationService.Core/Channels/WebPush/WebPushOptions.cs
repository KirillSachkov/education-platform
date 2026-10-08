namespace NotificationService.Core.Channels.WebPush;

/// <summary>
/// Опции Web Push (VAPID) / Web Push (VAPID) options. Биндятся из секции
/// <c>Notifications:WebPush</c>. Issue #342.
///
/// <see cref="PublicKey"/>/<see cref="PrivateKey"/> — VAPID keypair (base64url, P-256).
/// Публичный ключ также прокидывается фронту через <c>NEXT_PUBLIC_VAPID_PUBLIC_KEY</c>.
/// Приватный ключ — секрет (env / Infisical), в git не коммитится.
///
/// Если ключи не заданы (<see cref="IsConfigured"/> = <c>false</c>) — WebPush-канал не
/// регистрируется: сервис работает как раньше (InApp/Email/Telegram), push не доставляется.
/// </summary>
public sealed class WebPushOptions
{
    public const string SECTION = "Notifications:WebPush";

    /// <summary>Контакт для push-сервиса: <c>mailto:</c> или <c>https:</c> URI (VAPID Subject).</summary>
    public string Subject { get; set; } = "mailto:admin@sachkov-learn.net";

    /// <summary>VAPID public key (base64url, 65 bytes uncompressed P-256 point).</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>VAPID private key (base64url, 32 bytes).</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>Заданы ли оба ключа — гейт регистрации WebPush-канала.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}
