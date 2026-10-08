using AuthService.Contracts.AuthorSpaces;
using Core.HttpCommunication;
using Microsoft.Extensions.Logging;

namespace AuthService.Contracts.HttpCommunication;

internal sealed class AuthServiceClient : BaseHttpClient, IAuthServiceClient
{
    private const string SERVICE_NAME = "AuthService";

    public AuthServiceClient(
        HttpClient httpClient,
        ILogger<AuthServiceClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public Task<Result<AuthUserLookupDto, Error>> GetUserByEmailAsync(
        string email,
        CancellationToken cancellationToken)
        => GetAsync<AuthUserLookupDto>(
            $"/internal/users/by-email?email={Uri.EscapeDataString(email)}",
            cancellationToken);

    public Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> GetUsersByIdsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
        => PostAsync<InternalUsersBatchRequest, IReadOnlyList<AuthUserLookupDto>>(
            "/internal/users/batch",
            new InternalUsersBatchRequest(userIds),
            cancellationToken);

    public Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> SearchUsersAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
        => PostAsync<InternalUsersSearchRequest, IReadOnlyList<AuthUserLookupDto>>(
            "/internal/users/search",
            new InternalUsersSearchRequest(query, limit),
            cancellationToken);

    public Task<Result<AuthorSpacePublicResponse, Error>> GetAuthorSpaceBySlugAsync(
        string slug,
        CancellationToken cancellationToken)
        => GetAsync<AuthorSpacePublicResponse>(
            $"/users/author-spaces/by-slug/{Uri.EscapeDataString(slug)}",
            cancellationToken);

    public Task<Result<AuthorSpaceRouteResponse, Error>> GetAuthorSpaceByAuthorIdAsync(
        Guid authorId,
        CancellationToken cancellationToken)
        => GetAsync<AuthorSpaceRouteResponse>(
            $"/internal/users/{authorId}/author-space",
            cancellationToken);

    public Task<Result<UserIdsByGithubOrgResponse, Error>> GetUserIdsByGithubOrgAsync(
        string orgSlug,
        CancellationToken cancellationToken)
        => GetAsync<UserIdsByGithubOrgResponse>(
            $"/internal/users/by-github-org/{Uri.EscapeDataString(orgSlug)}",
            cancellationToken);

    public Task<Result<UserIdByGithubIdResponse, Error>> GetUserIdByGithubExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken)
        => GetAsync<UserIdByGithubIdResponse>(
            $"/internal/users/by-github-id/{Uri.EscapeDataString(externalId)}",
            cancellationToken);

    public Task<Result<UserGithubLoginResponse, Error>> GetUserGithubLoginAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => GetAsync<UserGithubLoginResponse>(
            $"/internal/users/{userId}/github-login/",
            cancellationToken);

    public Task<Result<AllUserIdsResponse, Error>> GetAllUserIdsAsync(
        Guid? afterId,
        int limit,
        bool? githubLinked,
        CancellationToken cancellationToken)
    {
        string url = afterId is null
            ? $"/internal/users/ids?limit={limit}"
            : $"/internal/users/ids?afterId={afterId}&limit={limit}";

        if (githubLinked is not null)
            url += githubLinked.Value ? "&githubLinked=true" : "&githubLinked=false";

        return GetAsync<AllUserIdsResponse>(url, cancellationToken);
    }
}
