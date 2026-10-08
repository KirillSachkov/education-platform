namespace AuthService.Contracts;

/// <summary>
///     Платформенный <c>UserId</c>, к которому привязан GitHub-аккаунт с данным
///     external id (numeric GitHub user id, как в Identity <c>user_logins.provider_key</c>
///     для провайдера <c>GitHub</c>). <see cref="UserId"/> = <c>null</c>, если ни один
///     пользователь не привязал этот GitHub-аккаунт. Used by AssignmentReviewService
///     webhook-recovery (#451) — резолвит юзера установки GitHub App, когда install-callback
///     не отработал.
/// </summary>
public sealed record UserIdByGithubIdResponse(
    string ExternalId,
    Guid? UserId);
