using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace EducationContentService.Core.Features.Plans;

/// <summary>
///     Decorator over <see cref="ICoursePricingClient"/>. Caches per-course plan pricing
///     in <see cref="HybridCache"/> with a positive entry for priced courses and a null
///     sentinel for courses that have no plan (negative cache). On inner-client outage
///     (HttpRequestException / TaskCanceledException / Polly BrokenCircuitException, or
///     a failed Result) the decorator soft-degrades — logs a warning and returns the
///     subset already in cache (empty if cold). Pricing is non-critical enrichment;
///     bubbling the failure would block course-catalog rendering.
/// </summary>
public sealed class CachedCoursePricingClient : ICoursePricingClient
{
    // Cache-key prefix anchors to the AccessService data origin — the cached payload
    // is per-course AccessService plan pricing, regardless of how this client is named.
    internal const string CACHE_KEY_PREFIX = "access:plan-for-course:";

    private readonly ICoursePricingClient _inner;
    private readonly HybridCache _cache;
    private readonly ILogger<CachedCoursePricingClient> _logger;
    private readonly HybridCacheEntryOptions _cacheOptions;

    public CachedCoursePricingClient(
        ICoursePricingClient inner,
        HybridCache cache,
        IOptions<AccessServiceOptions> options,
        ILogger<CachedCoursePricingClient> logger)
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

    public async Task<Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>> GetPlansForCoursesAsync(
        IReadOnlyCollection<Guid> courseIds,
        CancellationToken ct)
    {
        if (courseIds.Count == 0)
        {
            return Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(
                new Dictionary<Guid, CoursePricingDto>());
        }

        Dictionary<Guid, CoursePricingDto> result = new(courseIds.Count);
        List<Guid> missIds = new(courseIds.Count);

        // Probe per-id via GetOrCreateAsync with a null-returning factory. HybridCache has
        // no native TryGetAsync in Microsoft.Extensions.Caching.Hybrid 10.3 (only
        // GetOrCreateAsync / SetAsync / RemoveAsync / RemoveByTagAsync are exposed) — this
        // is the documented probe-with-null-sentinel workaround, also used by
        // CachedFileServiceClient. Per cache miss it costs 4 ops:
        //   1) probe via GetOrCreateAsync that writes a null placeholder,
        //   2) evict that placeholder so soft-degrade can't accidentally negative-cache,
        //   3) fetch real value from the inner client,
        //   4) write the real value (or an intentional null sentinel) via SetAsync.
        // Acceptable for the catalog hot-path: misses are rare after warm-up, the negative
        // cache (step 4 with null) covers no-plan courses, and keeping the same probe
        // shape as CachedFileServiceClient avoids drift across decorators. Revisit if a
        // future SDK ships a real TryGetAsync — at which point this collapses to 2 ops
        // (try-get + set on miss).
        // Factory called  → cache miss → enqueue for batch fetch.
        // Factory not called + cached==null → negative-cache hit (no plan for course).
        // Factory not called + cached!=null → positive cache hit.
        foreach (Guid id in courseIds.Distinct())
        {
            string key = $"{CACHE_KEY_PREFIX}{id}";
            bool factoryCalled = false;

            CoursePricingDto? cached = await _cache.GetOrCreateAsync<CoursePricingDto?>(
                key,
                _ =>
                {
                    factoryCalled = true;
                    return ValueTask.FromResult<CoursePricingDto?>(null);
                },
                _cacheOptions,
                cancellationToken: ct);

            if (factoryCalled)
            {
                // True miss — factory was invoked. Evict the placeholder null we just wrote
                // so a later soft-degraded path doesn't accidentally treat this id as
                // negative-cached. We will write the real value (or null sentinel) below.
                await _cache.RemoveAsync(key, ct);
                missIds.Add(id);
            }
            else if (cached is not null)
            {
                result[id] = cached;
            }
            // else: factory not called + cached null = negative cache hit, skip.
        }

        if (missIds.Count == 0)
            return Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(result);

        IReadOnlyDictionary<Guid, CoursePricingDto>? fetched = await TryFetchAsync(missIds, ct);
        if (fetched is null)
        {
            // Soft-degrade — return what we already have from cache. Do not cache nothing
            // for misses (so the next call retries the inner client once it recovers).
            return Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(result);
        }

        foreach (Guid id in missIds)
        {
            string key = $"{CACHE_KEY_PREFIX}{id}";
            if (fetched.TryGetValue(id, out CoursePricingDto? pricing))
            {
                result[id] = pricing;
                await _cache.SetAsync(key, pricing, _cacheOptions, cancellationToken: ct);
            }
            else
            {
                // Negative cache — store null so we don't refetch this id for CacheTtl.
                await _cache.SetAsync<CoursePricingDto?>(key, null, _cacheOptions, cancellationToken: ct);
            }
        }

        return Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(result);
    }

    private async Task<IReadOnlyDictionary<Guid, CoursePricingDto>?> TryFetchAsync(
        IReadOnlyCollection<Guid> missIds,
        CancellationToken ct)
    {
        try
        {
            Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error> fetched =
                await _inner.GetPlansForCoursesAsync(missIds, ct);

            if (fetched.IsFailure)
            {
                _logger.LogWarning(
                    "AccessService returned failure for {Count} course ids: {Error}. Soft-degrading to cached subset.",
                    missIds.Count, fetched.Error.GetMessage());
                return null;
            }

            return fetched.Value;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "AccessService HTTP failure while fetching {Count} course ids. Soft-degrading.",
                missIds.Count);
            return null;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "AccessService timeout while fetching {Count} course ids. Soft-degrading.",
                missIds.Count);
            return null;
        }
        catch (Exception ex) when (string.Equals(ex.GetType().Name, "BrokenCircuitException", StringComparison.Ordinal))
        {
            // Polly v7 (Microsoft.Extensions.Http.Polly) and Polly v8 (Polly.Core) both
            // expose BrokenCircuitException in different namespaces, causing CS0433 when
            // catching by type. Match by simple name — runtime check is cheap and covers
            // both Polly versions without forcing an extern alias on the whole project.
            _logger.LogWarning(
                ex,
                "AccessService circuit breaker open while fetching {Count} course ids. Soft-degrading.",
                missIds.Count);
            return null;
        }
    }
}
