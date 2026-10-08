namespace AccessService.Core.Features.Integrations.GitHubApp.Services;

/// <summary>
///     HTTP-фасад над теми GitHub API endpoint'ами, которые нужны для onboarding-flow.
///     Все методы получают installation token изнутри (через <see cref="IGitHubAppTokenService"/>).
/// </summary>
public interface IGitHubAppApiClient
{
    /// <summary>
    ///     <c>GET /app/installations/{id}</c>. Используется в callback'е после установки —
    ///     вычитываем `account.login` для записи в <c>author_github_installations.org_login</c>.
    /// </summary>
    Task<Result<InstallationDetail, Error>> GetInstallationAsync(
        long installationId, CancellationToken ct = default);

    /// <summary>
    ///     <c>GET /users/{login}</c> через installation-token.
    ///     Используется чтобы получить numeric `id` юзера (`invitee_id` для invite endpoint).
    /// </summary>
    Task<Result<long, Error>> GetUserIdByLoginAsync(
        long installationId, string login, CancellationToken ct = default);

    /// <summary>
    ///     <c>POST /orgs/{org}/invitations</c>. Возвращает invitation_id или
    ///     специальное `AlreadyMember` если юзер уже в org (422 + already_a_member).
    /// </summary>
    Task<CreateInvitationResult> CreateOrgInvitationAsync(
        long installationId, string orgLogin, long inviteeId, CancellationToken ct = default);

    /// <summary>
    ///     <c>GET /orgs/{org}/memberships/{username}</c>. Возвращает true если юзер
    ///     active member org'а. Используется в SyncInvitation (manual «я уже принял»).
    /// </summary>
    Task<Result<bool, Error>> IsOrgMemberAsync(
        long installationId, string orgLogin, string username, CancellationToken ct = default);

    /// <summary>
    ///     <c>DELETE /orgs/{org}/memberships/{username}</c>. Исключает пользователя из org'и
    ///     (или отзывает pending-приглашение). Используется при истечении/отзыве доступа (#687):
    ///     когда у юзера больше нет активного grant'а, покрывающего привязанный к org плану,
    ///     он удаляется из организации. Идемпотентно: 204 (удалён) и 404 (уже не член) → success.
    ///     Требует у GitHub App право управления членством (Organization members: Read &amp; write).
    /// </summary>
    Task<Result<bool, Error>> RemoveOrgMemberAsync(
        long installationId, string orgLogin, string username, CancellationToken ct = default);
}

public sealed record InstallationDetail(string AccountLogin, string AccountType);

public abstract record CreateInvitationResult
{
    public sealed record Created(long InvitationId) : CreateInvitationResult;

    public sealed record AlreadyMember : CreateInvitationResult;

    public sealed record UserNotFound : CreateInvitationResult;

    public sealed record TokenInvalid : CreateInvitationResult;

    public sealed record UnknownFailure(string Reason) : CreateInvitationResult;
}
