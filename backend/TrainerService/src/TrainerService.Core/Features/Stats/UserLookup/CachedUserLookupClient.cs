using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace TrainerService.Core.Features.Stats.UserLookup;

/// <summary>
///     HybridCache decorator over <see cref="IUserLookupClient"/> — caches resolved user credit per id
///     (positive entry per known user, null sentinel for unknown ids). Mirrors ECS
///     <c>CachedAuthorLookupClient</c>. Short TTL (~5 min, from <see cref="AuthServiceOptions.CacheTtl"/>)
///     because display names rarely change and the admin dashboard tolerates slight staleness. The inner
///     client soft-degrades to an empty dict on AuthService outage (never throws); this decorator only
///     adds caching, so it likewise never throws.
/// </summary>
public sealed class CachedUserLookupClient : IUserLookupClient
{
    internal const string CACHE_KEY_PREFIX = "trainer-user-lookup:";

    private readonly IUserLookupClient _inner;
    private readonly HybridCache _cache;
    private readonly HybridCacheEntryOptions _cacheOptions;

    public CachedUserLookupClient(
        IUserLookupClient inner,
        HybridCache cache,
        IOptions<AuthServiceOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _cacheOptions = new HybridCacheEntryOptions
        {
            Expiration = options.Value.CacheTtl,
            LocalCacheExpiration = options.Value.CacheTtl,
        };
    }

    public async Task<IReadOnlyDictionary<Guid, UserLookupDto>> GetUsersAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, UserLookupDto>();

        Dictionary<Guid, UserLookupDto> result = new(ids.Count);
        List<Guid> missIds = new(ids.Count);

        // Probe per-id with a null-returning factory (HybridCache 10.3 has no TryGetAsync — same
        // null-sentinel workaround as ECS CachedAuthorLookupClient): factory called → miss; not called +
        // null → negative-cache hit; not called + non-null → positive hit.
        foreach (Guid id in ids.Distinct())
        {
            string key = $"{CACHE_KEY_PREFIX}{id}";
            bool factoryCalled = false;

            UserLookupDto? cached = await _cache.GetOrCreateAsync<UserLookupDto?>(
                key,
                _ =>
                {
                    factoryCalled = true;
                    return ValueTask.FromResult<UserLookupDto?>(null);
                },
                _cacheOptions,
                cancellationToken: ct);

            if (factoryCalled)
            {
                // True miss — evict the placeholder null so it isn't read back as a negative-cache hit.
                await _cache.RemoveAsync(key, ct);
                missIds.Add(id);
            }
            else if (cached is not null)
            {
                result[id] = cached;
            }
            // else: negative-cache hit, skip.
        }

        if (missIds.Count == 0)
            return result;

        IReadOnlyDictionary<Guid, UserLookupDto> fetched = await _inner.GetUsersAsync(missIds, ct);

        foreach (Guid id in missIds)
        {
            string key = $"{CACHE_KEY_PREFIX}{id}";
            if (fetched.TryGetValue(id, out UserLookupDto? user))
            {
                result[id] = user;
                await _cache.SetAsync(key, user, _cacheOptions, cancellationToken: ct);
            }
            else
            {
                // Negative cache — AuthService doesn't know this id (or it was unavailable). The short
                // TTL bounds the staleness window on a transient outage.
                await _cache.SetAsync<UserLookupDto?>(key, null, _cacheOptions, cancellationToken: ct);
            }
        }

        return result;
    }
}
