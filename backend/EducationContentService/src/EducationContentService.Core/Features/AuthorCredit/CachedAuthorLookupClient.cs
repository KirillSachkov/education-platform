using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace EducationContentService.Core.Features.AuthorCredit;

/// <summary>
///     Decorator over <see cref="IAuthorLookupClient"/>. Caches per-author display credit
///     in <see cref="HybridCache"/> (positive entry per resolved author, null sentinel for
///     ids AuthService doesn't know — negative cache). On inner-client outage
///     (HttpRequestException / TaskCanceledException / Polly BrokenCircuitException, or a
///     failed Result) the decorator soft-degrades — logs a warning and returns the subset
///     already in cache (empty if cold). Author credit is non-critical enrichment; bubbling
///     the failure would block course-catalog / detail rendering.
/// </summary>
public sealed class CachedAuthorLookupClient : IAuthorLookupClient
{
    internal const string CACHE_KEY_PREFIX = "author-credit:";

    private readonly IAuthorLookupClient _inner;
    private readonly HybridCache _cache;
    private readonly ILogger<CachedAuthorLookupClient> _logger;
    private readonly HybridCacheEntryOptions _cacheOptions;

    public CachedAuthorLookupClient(
        IAuthorLookupClient inner,
        HybridCache cache,
        IOptions<AuthServiceOptions> options,
        ILogger<CachedAuthorLookupClient> logger)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
        _cacheOptions = new HybridCacheEntryOptions
        {
            Expiration = options.Value.CacheTtl,
            LocalCacheExpiration = options.Value.CacheTtl,
        };
    }

    public async Task<Result<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>> GetAuthorsByIdsAsync(
        IReadOnlyCollection<Guid> authorIds,
        CancellationToken ct)
    {
        if (authorIds.Count == 0)
        {
            return Result.Success<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>(
                new Dictionary<Guid, AuthorCreditDto>());
        }

        Dictionary<Guid, AuthorCreditDto> result = new(authorIds.Count);
        List<Guid> missIds = new(authorIds.Count);

        // Probe per-id with a null-returning factory (same null-sentinel workaround as
        // CachedCoursePricingClient / CachedFileServiceClient — HybridCache 10.3 has no
        // TryGetAsync). Factory called → miss → fetch from inner; factory not called +
        // cached==null → negative-cache hit; factory not called + cached!=null → hit.
        foreach (Guid id in authorIds.Distinct())
        {
            string key = $"{CACHE_KEY_PREFIX}{id}";
            bool factoryCalled = false;

            AuthorCreditDto? cached = await _cache.GetOrCreateAsync<AuthorCreditDto?>(
                key,
                _ =>
                {
                    factoryCalled = true;
                    return ValueTask.FromResult<AuthorCreditDto?>(null);
                },
                _cacheOptions,
                cancellationToken: ct);

            if (factoryCalled)
            {
                // True miss — evict the placeholder null so a later soft-degraded path
                // doesn't treat this id as negative-cached; real value written below.
                await _cache.RemoveAsync(key, ct);
                missIds.Add(id);
            }
            else if (cached is not null)
            {
                result[id] = cached;
            }
            // else: negative cache hit, skip.
        }

        if (missIds.Count == 0)
            return Result.Success<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>(result);

        IReadOnlyDictionary<Guid, AuthorCreditDto>? fetched = await TryFetchAsync(missIds, ct);
        if (fetched is null)
        {
            // Soft-degrade — return cached subset, don't negative-cache the misses (retry next call).
            return Result.Success<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>(result);
        }

        foreach (Guid id in missIds)
        {
            string key = $"{CACHE_KEY_PREFIX}{id}";
            if (fetched.TryGetValue(id, out AuthorCreditDto? credit))
            {
                result[id] = credit;
                await _cache.SetAsync(key, credit, _cacheOptions, cancellationToken: ct);
            }
            else
            {
                // Negative cache — AuthService doesn't know this id (or it has no profile).
                await _cache.SetAsync<AuthorCreditDto?>(key, null, _cacheOptions, cancellationToken: ct);
            }
        }

        return Result.Success<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>(result);
    }

    private async Task<IReadOnlyDictionary<Guid, AuthorCreditDto>?> TryFetchAsync(
        IReadOnlyCollection<Guid> missIds,
        CancellationToken ct)
    {
        try
        {
            Result<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error> fetched =
                await _inner.GetAuthorsByIdsAsync(missIds, ct);

            if (fetched.IsFailure)
            {
                _logger.LogWarning(
                    "AuthService returned failure for {Count} author ids: {Error}. Soft-degrading to cached subset.",
                    missIds.Count, fetched.Error.GetMessage());
                return null;
            }

            return fetched.Value;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "AuthService HTTP failure while fetching {Count} author ids. Soft-degrading.",
                missIds.Count);
            return null;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "AuthService timeout while fetching {Count} author ids. Soft-degrading.",
                missIds.Count);
            return null;
        }
        catch (Exception ex) when (string.Equals(ex.GetType().Name, "BrokenCircuitException", StringComparison.Ordinal))
        {
            // Match Polly BrokenCircuitException by simple name — covers both Polly v7/v8
            // without forcing an extern alias (same approach as CachedCoursePricingClient).
            _logger.LogWarning(
                ex,
                "AuthService circuit breaker open while fetching {Count} author ids. Soft-degrading.",
                missIds.Count);
            return null;
        }
    }
}
