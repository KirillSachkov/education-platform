namespace AccessService.Contracts.Integrations.GitHub;

/// <summary>
///     Body для <c>POST /access/integrations/github/invitations/</c>.
///     Студент запрашивает приглашение в org для конкретного плана,
///     на который у него есть active grant.
///     <see cref="GithubLogin"/> — публичный GitHub username юзера (без префикса
///     <c>@</c>). Резолвится фронтом из <c>/users/me/profile</c> (поле
///     <c>gitHubUrl</c>: <c>https://github.com/{login}</c>). На бэке не доверяем
///     слепо — backend сверяет с GitHub API через installation token.
/// </summary>
public sealed record CreateGithubInvitationRequest(Guid PlanId, string GithubLogin);

/// <summary>
///     Body для <c>POST /access/integrations/github/install-redirect/</c>.
///     <see cref="PlanId"/> — опциональный return-context: после установки автор
///     редиректится на <c>/author/plans/{planId}/edit</c>. Если не передан —
///     общий redirect в <c>FrontendInstallReturnUrl</c>.
/// </summary>
public sealed record InstallRedirectRequest(Guid? PlanId);
