namespace Shared.GitHubApp;

/// <summary>
///     Минимальная shape конфигурации GitHub App'а. Используется
///     <see cref="GitHubAppTokenService"/> и <see cref="GitHubAppPrivateKey"/>.
///
///     В Phase 2 это просто POCO без DI-binding helper'ов и без section-name'а —
///     каждый сервис на текущем этапе биндит свой собственный options (с per-service
///     section name). Phase 3 добавит общий <c>AddSharedGitHubApp</c> extension
///     с binding'ом + рекомендованным <c>SECTION_NAME</c>.
/// </summary>
public sealed class GitHubAppOptions
{
    /// <summary>
    ///     App slug (URL-segment в <c>https://github.com/apps/{slug}/installations/new</c>).
    ///     Public, не секрет — используется в User-Agent header'е.
    /// </summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>App ID (numeric, public). Используется как <c>iss</c> в App-level JWT.</summary>
    public long AppId { get; init; }

    /// <summary>
    ///     RSA private key (PEM-формат, base64-encoded). Используется для подписи
    ///     App-level JWT (RS256) при обмене на installation token.
    /// </summary>
    public string PrivateKeyPemBase64 { get; init; } = string.Empty;

    /// <summary>
    ///     Webhook secret. Используется <see cref="WebhookSignatureVerifier"/>
    ///     для HMAC-SHA256 проверки <c>X-Hub-Signature-256</c> header'а.
    /// </summary>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>
    ///     <c>true</c> если все обязательные для подписи JWT и проверки webhook'а
    ///     поля заполнены. Сервис-уровень проверяет это перед каждым API-вызовом
    ///     и возвращает domain error если App не сконфигурирован.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(Slug)
        && AppId > 0
        && !string.IsNullOrEmpty(PrivateKeyPemBase64)
        && !string.IsNullOrEmpty(WebhookSecret);
}
