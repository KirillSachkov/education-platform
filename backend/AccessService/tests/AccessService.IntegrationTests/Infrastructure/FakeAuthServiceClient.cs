using AuthService.Contracts;
using AuthService.Contracts.AuthorSpaces;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory replacement for <see cref="IAuthServiceClient"/> used by AccessService
/// integration tests. By default returns empty results; tests that need specific
/// enrichment (e.g., ListPlanGrants user-info) can preload <see cref="UsersById"/>.
/// </summary>
public sealed class FakeAuthServiceClient : IAuthServiceClient
{
    public Dictionary<Guid, AuthUserLookupDto> UsersById { get; } = [];
    public Dictionary<Guid, string> GithubLoginsByUserId { get; } = [];

    /// <summary>
    /// Org-slug → user IDs membership, backing <see cref="GetUserIdsByGithubOrgAsync"/>.
    /// Mirrors AuthService's <c>auth.user_github_orgs</c> reverse lookup; tests preload it to
    /// simulate "user already in the plan's GitHub org" (green-card path, #448). Case-insensitive
    /// keys because the real handler lowercases the slug.
    /// </summary>
    public Dictionary<string, List<Guid>> UserIdsByGithubOrg { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void Reset()
    {
        UsersById.Clear();
        GithubLoginsByUserId.Clear();
        UserIdsByGithubOrg.Clear();
    }

    public Task<Result<AuthUserLookupDto, Error>> GetUserByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        AuthUserLookupDto? match = UsersById.Values.FirstOrDefault(
            u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match is null
            ? Result.Failure<AuthUserLookupDto, Error>(Error.NotFound("auth.user.not.found", "Не найден"))
            : Result.Success<AuthUserLookupDto, Error>(match));
    }

    public Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> GetUsersByIdsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AuthUserLookupDto> matches = userIds
            .Select(id => UsersById.TryGetValue(id, out AuthUserLookupDto? u) ? u : null)
            .OfType<AuthUserLookupDto>()
            .ToList();
        return Task.FromResult(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(matches));
    }

    public Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> SearchUsersAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AuthUserLookupDto> matches = UsersById.Values
            .Where(u =>
                (u.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.TelegramUsername?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(limit)
            .ToList();
        return Task.FromResult(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(matches));
    }

    public Task<Result<AuthorSpacePublicResponse, Error>> GetAuthorSpaceBySlugAsync(
        string slug,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<AuthorSpacePublicResponse, Error>(
            Error.NotFound("auth.space.not.found", "Не найдено")));

    public Task<Result<AuthorSpaceRouteResponse, Error>> GetAuthorSpaceByAuthorIdAsync(
        Guid authorId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<AuthorSpaceRouteResponse, Error>(
            Error.NotFound("auth.space.not.found", "Не найдено")));

    public Task<Result<UserIdsByGithubOrgResponse, Error>> GetUserIdsByGithubOrgAsync(
        string orgSlug,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> userIds = UserIdsByGithubOrg.TryGetValue(orgSlug, out List<Guid>? ids)
            ? ids
            : [];
        return Task.FromResult(Result.Success<UserIdsByGithubOrgResponse, Error>(
            new UserIdsByGithubOrgResponse(orgSlug.ToLowerInvariant(), userIds)));
    }

    public Task<Result<UserIdByGithubIdResponse, Error>> GetUserIdByGithubExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken)
    {
        // GitHub external-id lookup is ARS-only (#451); AccessService tests never exercise it.
        return Task.FromResult(Result.Success<UserIdByGithubIdResponse, Error>(
            new UserIdByGithubIdResponse(externalId, null)));
    }

    public Task<Result<UserGithubLoginResponse, Error>> GetUserGithubLoginAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        GithubLoginsByUserId.TryGetValue(userId, out string? login);
        return Task.FromResult(Result.Success<UserGithubLoginResponse, Error>(
            new UserGithubLoginResponse(userId, login)));
    }

    // Weekly digest (#532) is NotificationService-only; AccessService tests never exercise it.
    public Task<Result<AllUserIdsResponse, Error>> GetAllUserIdsAsync(
        Guid? afterId,
        int limit,
        bool? githubLinked,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Success<AllUserIdsResponse, Error>(new AllUserIdsResponse([], null)));
}
