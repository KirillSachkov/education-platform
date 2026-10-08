namespace AssignmentReviewService.Core.Features.Installations.Services;

/// <summary>
///     Конфигурация GitHub App для AssignmentReviewService. Зеркалит
///     <c>AccessService.Core.Features.Integrations.GitHubApp.GitHubAppOptions</c>
///     по форме, но это ОТДЕЛЬНЫЙ App (issue #15) — другие permissions
///     (Pull requests R&amp;W вместо Members:Write) и другой webhook URL.
///
///     Все поля живут в Infisical / `.env`. Section: <c>AssignmentReview:GitHub</c>.
/// </summary>
public sealed class GitHubAppOptions
{
    public const string SECTION_NAME = "AssignmentReview:GitHub";

    /// <summary>App slug — public, не секрет.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>App ID (numeric, public). 0 → integration отключена (skeleton mode / dev).</summary>
    public long AppId { get; init; }

    /// <summary>OAuth Client ID (для будущего user-authorization flow).</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Private key (RSA PEM, base64-encoded). RS256 signing для App JWT.</summary>
    public string PrivateKeyPemBase64 { get; init; } = string.Empty;

    /// <summary>Webhook secret для HMAC-SHA256 verification (Phase 4 use).</summary>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>True если все critical поля заданы. Provider-методы возвращают error если false.</summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(Slug)
        && AppId > 0
        && !string.IsNullOrEmpty(PrivateKeyPemBase64)
        && !string.IsNullOrEmpty(WebhookSecret);
}
