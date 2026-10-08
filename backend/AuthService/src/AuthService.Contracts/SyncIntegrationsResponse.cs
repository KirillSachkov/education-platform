namespace AuthService.Contracts;

/// <summary>
///     Ответ XHR-эндпоинта <c>POST /users/me/integrations/sync</c>: список org-slug'ов,
///     совпавших с привязанными к курсам, и флаги, какие из под-флоу запустились.
///     Использует кэш <c>auth.user_github_orgs</c> — без обращения к GitHub API.
///     Реальное зачисление и инвайты прилетают асинхронно через события.
/// </summary>
public sealed record SyncIntegrationsResponse(
    IReadOnlyList<string> MatchedGithubOrgs,
    bool GithubSyncTriggered,
    bool TelegramLinked
);
