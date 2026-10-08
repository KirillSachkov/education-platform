using AuthService.Contracts.AuthorSpaces;

namespace AuthService.Contracts.HttpCommunication;

public interface IAuthServiceClient
{
    Task<Result<AuthUserLookupDto, Error>> GetUserByEmailAsync(
        string email,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> GetUsersByIdsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> SearchUsersAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);

    Task<Result<AuthorSpacePublicResponse, Error>> GetAuthorSpaceBySlugAsync(
        string slug,
        CancellationToken cancellationToken);

    Task<Result<AuthorSpaceRouteResponse, Error>> GetAuthorSpaceByAuthorIdAsync(
        Guid authorId,
        CancellationToken cancellationToken);

    Task<Result<UserIdsByGithubOrgResponse, Error>> GetUserIdsByGithubOrgAsync(
        string orgSlug,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Платформенный <c>UserId</c>, привязанный к GitHub-аккаунту с данным external id
    ///     (numeric GitHub user id). <c>UserId == null</c>, если привязки нет. Internal-only,
    ///     вызывается под service-токеном. Used by ARS webhook-recovery установки GitHub App (#451).
    /// </summary>
    Task<Result<UserIdByGithubIdResponse, Error>> GetUserIdByGithubExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken);

    Task<Result<UserGithubLoginResponse, Error>> GetUserGithubLoginAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Keyset-страница id всех НЕ залоченных пользователей (#532). Internal-only,
    ///     под service-токеном. Используется NotificationService для платформенного
    ///     еженедельного дайджеста всем зарегистрированным.
    ///     <para>
    ///         <paramref name="githubLinked"/> (#699): <c>true</c> — только пользователи
    ///         с GitHub-привязкой (<c>user_logins.login_provider = 'GitHub'</c>),
    ///         <c>false</c> — только без неё, <c>null</c> — все. Аудитории кампаний
    ///         email-only-login (epic #696).
    ///     </para>
    /// </summary>
    Task<Result<AllUserIdsResponse, Error>> GetAllUserIdsAsync(
        Guid? afterId,
        int limit,
        bool? githubLinked,
        CancellationToken cancellationToken);
}
