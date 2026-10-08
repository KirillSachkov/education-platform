using AuthService.Contracts;
using AuthService.Contracts.AuthorSpaces;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace ProgressService.IntegrationTests.Infrastructure;

public sealed class MockAuthServiceClient : IAuthServiceClient
{
    private readonly Dictionary<Guid, AuthUserLookupDto> _usersById = [];
    private readonly Dictionary<string, Guid> _userIdsByEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Guid>> _userIdsByGithubOrg = new(StringComparer.Ordinal);

    public void Reset()
    {
        _usersById.Clear();
        _userIdsByEmail.Clear();
        _userIdsByGithubOrg.Clear();
    }

    public void AddGithubOrgMembership(string orgSlug, Guid userId)
    {
        string normalized = orgSlug.ToLowerInvariant();
        if (!_userIdsByGithubOrg.TryGetValue(normalized, out List<Guid>? users))
        {
            users = [];
            _userIdsByGithubOrg[normalized] = users;
        }

        if (!users.Contains(userId))
            users.Add(userId);
    }

    public void AddUser(
        Guid userId,
        string email,
        string? name = null,
        string? username = null,
        Guid? avatarId = null,
        string? telegramUsername = null)
    {
        var user = new AuthUserLookupDto(userId, name, username, email, avatarId, telegramUsername);
        _usersById[userId] = user;
        _userIdsByEmail[email] = userId;
    }

    public Task<Result<AuthUserLookupDto, Error>> GetUserByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        if (_userIdsByEmail.TryGetValue(email, out Guid userId) &&
            _usersById.TryGetValue(userId, out AuthUserLookupDto? user) &&
            user is not null)
        {
            return Task.FromResult(Result.Success<AuthUserLookupDto, Error>(user));
        }

        return Task.FromResult(Result.Failure<AuthUserLookupDto, Error>(
            Error.NotFound("auth.user.not.found", "Пользователь не найден")));
    }

    public Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> GetUsersByIdsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AuthUserLookupDto> users = userIds
            .Where(_usersById.ContainsKey)
            .Select(id => _usersById[id])
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(users));
    }

    public Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> SearchUsersAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AuthUserLookupDto> matches = _usersById.Values
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
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Failure<AuthorSpacePublicResponse, Error>(
            Error.NotFound("author_space.not_found", "Пространство автора не найдено")));
    }

    public Task<Result<AuthorSpaceRouteResponse, Error>> GetAuthorSpaceByAuthorIdAsync(
        Guid authorId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Failure<AuthorSpaceRouteResponse, Error>(
            Error.NotFound("author_space.not_found", "Пространство автора не найдено")));
    }

    public Task<Result<UserIdsByGithubOrgResponse, Error>> GetUserIdsByGithubOrgAsync(
        string orgSlug,
        CancellationToken cancellationToken)
    {
        string normalized = orgSlug.ToLowerInvariant();
        IReadOnlyList<Guid> users = _userIdsByGithubOrg.TryGetValue(normalized, out List<Guid>? list)
            ? list
            : Array.Empty<Guid>();

        return Task.FromResult(Result.Success<UserIdsByGithubOrgResponse, Error>(
            new UserIdsByGithubOrgResponse(normalized, users)));
    }

    public Task<Result<UserIdByGithubIdResponse, Error>> GetUserIdByGithubExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken)
    {
        // GitHub external-id lookup is ARS-only (#451); ProgressService tests never exercise it.
        return Task.FromResult(Result.Success<UserIdByGithubIdResponse, Error>(
            new UserIdByGithubIdResponse(externalId, null)));
    }

    public Task<Result<UserGithubLoginResponse, Error>> GetUserGithubLoginAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<UserGithubLoginResponse, Error>(
            new UserGithubLoginResponse(userId, null)));

    // Weekly digest (#532) is NotificationService-only; ProgressService tests never exercise it.
    public Task<Result<AllUserIdsResponse, Error>> GetAllUserIdsAsync(
        Guid? afterId,
        int limit,
        bool? githubLinked,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Success<AllUserIdsResponse, Error>(new AllUserIdsResponse([], null)));
}
