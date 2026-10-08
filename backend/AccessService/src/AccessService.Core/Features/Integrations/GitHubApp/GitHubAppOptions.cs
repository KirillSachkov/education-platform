namespace AccessService.Core.Features.Integrations.GitHubApp;

/// <summary>
///     Конфигурация GitHub App для авто-приглашения юзеров в org автора.
///     Все поля живут в Infisical / `.env` (App private key — секрет).
/// </summary>
public sealed class GitHubAppOptions
{
    public const string SECTION_NAME = "GitHubApp";

    /// <summary>
    ///     App slug (URL-segment в `https://github.com/apps/{slug}/installations/new`).
    ///     Public, не секрет.
    /// </summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>App ID (numeric, public).</summary>
    public long AppId { get; init; }

    /// <summary>OAuth Client ID (для будущего user-authorization flow). Сейчас не используется.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    ///     Private key (RSA PEM, base64-encoded). Используется для подписи JWT
    ///     (RS256) при запросе installation tokens у GitHub API.
    /// </summary>
    public string PrivateKeyPemBase64 { get; init; } = string.Empty;

    /// <summary>
    ///     Webhook secret. Используется для HMAC-SHA256 verification incoming
    ///     `X-Hub-Signature-256` заголовка.
    /// </summary>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>
    ///     Frontend base URL для redirect-callback после install.
    ///     По умолчанию `/author/plans/{planId}/edit?github=connected`.
    /// </summary>
    public string FrontendInstallReturnUrl { get; init; } = "/";

    public bool IsConfigured =>
        !string.IsNullOrEmpty(Slug)
        && AppId > 0
        && !string.IsNullOrEmpty(PrivateKeyPemBase64)
        && !string.IsNullOrEmpty(WebhookSecret);
}
