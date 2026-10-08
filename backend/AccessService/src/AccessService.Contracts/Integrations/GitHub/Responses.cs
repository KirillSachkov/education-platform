namespace AccessService.Contracts.Integrations.GitHub;

/// <summary>
///     Ответ <c>POST /access/integrations/github/install-redirect/</c>.
///     Frontend редиректит на <see cref="Url"/> — там GitHub проводит автора
///     через installation flow.
/// </summary>
public sealed record InstallRedirectResponse(string Url);

/// <summary>
///     Состояние GitHub App-установки автора. Используется фронтом на странице
///     редактирования плана для индикации «подключено / не подключено».
/// </summary>
public sealed record GithubInstallationStatusResponse(
    bool IsInstalled,
    string? OrgLogin,
    bool IsSuspended,
    DateTimeOffset? InstalledAt);

/// <summary>
///     Состояние invitation для пары (user, plan). 404-equivalent — null,
///     иначе — текущий статус + опциональные timestamps.
/// </summary>
public sealed record GithubInvitationStatusResponse(
    Guid Id,
    string Status,
    string OrgLogin,
    string GithubLogin,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? LastSyncedAt);
