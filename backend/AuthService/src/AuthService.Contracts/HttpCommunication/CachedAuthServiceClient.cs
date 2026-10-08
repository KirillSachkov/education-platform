using AuthService.Contracts.AuthorSpaces;
using Microsoft.Extensions.Caching.Hybrid;

namespace AuthService.Contracts.HttpCommunication;

public sealed class CachedAuthServiceClient : IAuthServiceClient
{
    public const string USER_CACHE_KEY_PREFIX = "auth-service:user:";
    private const string SPACE_CACHE_KEY_PREFIX = "auth-service:author-space:";
    private const string SPACE_BY_AUTHOR_CACHE_KEY_PREFIX = "auth-service:author-space-by-author:";

    private readonly IAuthServiceClient _inner;
    private readonly HybridCache _cache;

    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1),
    };

    public CachedAuthServiceClient(IAuthServiceClient inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    // Not cached — called from mutation paths where freshness is required.
    public Task<Result<AuthUserLookupDto, Error>> GetUserByEmailAsync(
        string email,
        CancellationToken cancellationToken)
        => _inner.GetUserByEmailAsync(email, cancellationToken);

    // Security-sensitive mutation lookup: never serve a stale linked identity from cache.
    public Task<Result<UserGithubLoginResponse, Error>> GetUserGithubLoginAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => _inner.GetUserGithubLoginAsync(userId, cancellationToken);

    // Not cached — search results would explode the keyspace and TTL'd query strings
    // can change between keystrokes; the inner HTTP call is cheap (limit=10).
    public Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> SearchUsersAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
        => _inner.SearchUsersAsync(query, limit, cancellationToken);

    public async Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> GetUsersByIdsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>([]);
        }

        // Probe all cache keys in parallel.
        // HybridCache has no TryGet — probe via GetOrCreateAsync with a null-returning factory.
        // On cache hit the factory is never called; on miss it fires and we track it via flag.
        Task<CacheProbeResult>[] probeTasks = new Task<CacheProbeResult>[userIds.Count];
        for (int i = 0; i < userIds.Count; i++)
        {
            probeTasks[i] = ProbeCacheAsync(userIds[i], cancellationToken);
        }

        CacheProbeResult[] probeResults = await Task.WhenAll(probeTasks);

        List<AuthUserLookupDto> results = new(userIds.Count);
        List<Guid> uncachedIds = [];

        foreach (CacheProbeResult probe in probeResults)
        {
            if (probe.Cached is not null)
            {
                results.Add(probe.Cached);
            }
            else
            {
                uncachedIds.Add(probe.UserId);
            }
        }

        if (uncachedIds.Count == 0)
        {
            return results;
        }

        // Single batch HTTP call for all uncached users
        Result<IReadOnlyList<AuthUserLookupDto>, Error> fetchResult =
            await _inner.GetUsersByIdsAsync(uncachedIds, cancellationToken);

        if (fetchResult.IsFailure)
        {
            // Degrade gracefully — return whatever we got from cache
            return results;
        }

        foreach (AuthUserLookupDto user in fetchResult.Value)
        {
            string key = $"{USER_CACHE_KEY_PREFIX}{user.UserId}";
            await _cache.SetAsync(key, user, _cacheOptions, cancellationToken: cancellationToken);
            results.Add(user);
        }

        return results;
    }

    private async Task<CacheProbeResult> ProbeCacheAsync(Guid userId, CancellationToken cancellationToken)
    {
        string key = $"{USER_CACHE_KEY_PREFIX}{userId}";
        bool factoryCalled = false;

        AuthUserLookupDto? cached = await _cache.GetOrCreateAsync<AuthUserLookupDto?>(
            key,
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult<AuthUserLookupDto?>(null);
            },
            _cacheOptions,
            cancellationToken: cancellationToken);

        if (!factoryCalled && cached is not null)
        {
            return new CacheProbeResult(userId, cached);
        }

        await _cache.RemoveAsync(key, cancellationToken);
        return new CacheProbeResult(userId, null);
    }

    private readonly record struct CacheProbeResult(Guid UserId, AuthUserLookupDto? Cached);

    public async Task<Result<AuthorSpacePublicResponse, Error>> GetAuthorSpaceBySlugAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        string key = $"{SPACE_CACHE_KEY_PREFIX}{slug}";

        // Try cache first; on miss call inner client
        bool factoryCalled = false;
        AuthorSpacePublicResponse? cached = await _cache.GetOrCreateAsync<AuthorSpacePublicResponse?>(
            key,
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult<AuthorSpacePublicResponse?>(null);
            },
            _cacheOptions,
            cancellationToken: cancellationToken);

        if (!factoryCalled && cached is not null)
            return cached;

        await _cache.RemoveAsync(key, cancellationToken);

        Result<AuthorSpacePublicResponse, Error> result =
            await _inner.GetAuthorSpaceBySlugAsync(slug, cancellationToken);

        if (result.IsSuccess)
            await _cache.SetAsync(key, result.Value, _cacheOptions, cancellationToken: cancellationToken);

        return result;
    }

    public async Task<Result<AuthorSpaceRouteResponse, Error>> GetAuthorSpaceByAuthorIdAsync(
        Guid authorId,
        CancellationToken cancellationToken)
    {
        string key = $"{SPACE_BY_AUTHOR_CACHE_KEY_PREFIX}{authorId}";
        bool factoryCalled = false;

        AuthorSpaceRouteResponse? cached = await _cache.GetOrCreateAsync<AuthorSpaceRouteResponse?>(
            key,
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult<AuthorSpaceRouteResponse?>(null);
            },
            _cacheOptions,
            cancellationToken: cancellationToken);

        if (!factoryCalled && cached is not null)
            return cached;

        await _cache.RemoveAsync(key, cancellationToken);

        Result<AuthorSpaceRouteResponse, Error> result =
            await _inner.GetAuthorSpaceByAuthorIdAsync(authorId, cancellationToken);

        if (result.IsSuccess)
            await _cache.SetAsync(key, result.Value, _cacheOptions, cancellationToken: cancellationToken);

        return result;
    }

    // Not cached — used from background reconciliation and event handlers where freshness matters.
    public Task<Result<UserIdsByGithubOrgResponse, Error>> GetUserIdsByGithubOrgAsync(
        string orgSlug,
        CancellationToken cancellationToken)
        => _inner.GetUserIdsByGithubOrgAsync(orgSlug, cancellationToken);

    // Not cached — low-frequency webhook-recovery path; a stale "no user" result would
    // silently skip recovering a freshly-linked GitHub account.
    public Task<Result<UserIdByGithubIdResponse, Error>> GetUserIdByGithubExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken)
        => _inner.GetUserIdByGithubExternalIdAsync(externalId, cancellationToken);

    // Not cached — weekly digest / campaign background passes (#532, #699), rare paged sweeps.
    public Task<Result<AllUserIdsResponse, Error>> GetAllUserIdsAsync(
        Guid? afterId,
        int limit,
        bool? githubLinked,
        CancellationToken cancellationToken)
        => _inner.GetAllUserIdsAsync(afterId, limit, githubLinked, cancellationToken);
}
