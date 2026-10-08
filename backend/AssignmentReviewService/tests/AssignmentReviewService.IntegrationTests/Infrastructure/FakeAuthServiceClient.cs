using AuthService.Contracts;
using AuthService.Contracts.AuthorSpaces;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AssignmentReviewService.IntegrationTests.Infrastructure;

/// <summary>
///     In-memory fake <see cref="IAuthServiceClient"/>. Webhook-recovery tests (#451)
///     конфигурируют только <see cref="UserIdByGithubExternalIdHandler"/> — резолв юзера
///     по GitHub external id. Прочие методы интерфейса в ARS-тестах не используются.
/// </summary>
public sealed class FakeAuthServiceClient : IAuthServiceClient
{
    /// <summary>
    ///     Маппинг GitHub external id → платформенный UserId. Возврат <c>null</c> =
    ///     «GitHub-аккаунт не привязан к платформе». Default (handler не задан) — null.
    /// </summary>
    public Func<string, Guid?>? UserIdByGithubExternalIdHandler { get; set; }

    /// <summary>
    ///     Batch-lookup id → <see cref="AuthUserLookupDto"/> (#713 — резолв имени студента для
    ///     <c>StudentPrQuestionAsked.StudentName</c>). Default (handler не задан) — пустой список
    ///     (имя не резолвится, событие публикуется без имени).
    /// </summary>
    public Func<IReadOnlyList<Guid>, IReadOnlyList<AuthUserLookupDto>>? UsersByIdsHandler { get; set; }

    public Task<Result<UserIdByGithubIdResponse, Error>> GetUserIdByGithubExternalIdAsync(
        string externalId, CancellationToken cancellationToken)
    {
        Guid? userId = UserIdByGithubExternalIdHandler?.Invoke(externalId);
        return Task.FromResult(
            Result.Success<UserIdByGithubIdResponse, Error>(
                new UserIdByGithubIdResponse(externalId, userId)));
    }

    public Task<Result<UserGithubLoginResponse, Error>> GetUserGithubLoginAsync(
        Guid userId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public void Reset()
    {
        UserIdByGithubExternalIdHandler = null;
        UsersByIdsHandler = null;
    }

    // ── Не используются в ARS-тестах ──────────────────────────────────────
    public Task<Result<AuthUserLookupDto, Error>> GetUserByEmailAsync(
        string email, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> GetUsersByIdsAsync(
        IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    {
        IReadOnlyList<AuthUserLookupDto> users =
            UsersByIdsHandler?.Invoke(userIds) ?? [];
        return Task.FromResult(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(users));
    }

    public Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> SearchUsersAsync(
        string query, int limit, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<Result<AuthorSpacePublicResponse, Error>> GetAuthorSpaceBySlugAsync(
        string slug, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<Result<AuthorSpaceRouteResponse, Error>> GetAuthorSpaceByAuthorIdAsync(
        Guid authorId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<Result<UserIdsByGithubOrgResponse, Error>> GetUserIdsByGithubOrgAsync(
        string orgSlug, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<Result<AllUserIdsResponse, Error>> GetAllUserIdsAsync(
        Guid? afterId, int limit, bool? githubLinked, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
