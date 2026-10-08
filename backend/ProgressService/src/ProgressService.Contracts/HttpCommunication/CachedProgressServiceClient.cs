using Microsoft.Extensions.Caching.Hybrid;

namespace ProgressService.Contracts.HttpCommunication;

/// <summary>
///     Decorator над <see cref="IProgressServiceClient"/>: кэширует счётчики просмотров в
///     <see cref="HybridCache"/> (Redis + L1) с TTL 5 минут. Batch-запрос делит id'шники
///     на cache hits/misses и идёт в inner client только за недостающими — паттерн идентичен
///     <c>CachedFileServiceClient</c>.
///
///     5 минут — компромисс между «бейдж быстро растёт на ходу» и «не клогаем PG счётом».
///     Если бейдж за это время отстаёт — нет беды: counter не критичен к latency.
/// </summary>
public sealed class CachedProgressServiceClient : IProgressServiceClient
{
    public const string MATERIAL_VIEWS_COUNT_CACHE_KEY_PREFIX = "progress-service:material-views-count:";

    /// <summary>
    ///     Sentinel «значения нет в кэше». Используется в probe-фабрике, чтобы отличить
    ///     hit'ы (включая count=0) от miss'ов без двусмысленности `long?` + null. Real
    ///     счётчики всегда ≥0; -1 в кэш не попадает — записывается только результат
    ///     <c>SetAsync</c> из BatchFetch'а.
    /// </summary>
    private const long CACHE_MISS_SENTINEL = -1L;

    private readonly IProgressServiceClient _inner;
    private readonly HybridCache _cache;

    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1),
    };

    public CachedProgressServiceClient(IProgressServiceClient inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<Result<IReadOnlyDictionary<Guid, long>, Error>> GetMaterialViewsCountsAsync(
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken)
    {
        if (materialIds.Count == 0)
        {
            return Result.Success<IReadOnlyDictionary<Guid, long>, Error>(new Dictionary<Guid, long>());
        }

        Dictionary<Guid, long> result = new(materialIds.Count);
        List<Guid> missIds = new(materialIds.Count);

        // Probe через GetOrCreateAsync<long> + sentinel: factory возвращает -1 (miss),
        // ≥0 значения приходят либо из cached SetAsync'а, либо после BatchFetch'а ниже.
        // HybridCache не даёт TryGet — это единственный надёжный способ для value-type'а.
        foreach (Guid id in materialIds)
        {
            string key = $"{MATERIAL_VIEWS_COUNT_CACHE_KEY_PREFIX}{id}";
            bool factoryCalled = false;

            long cached = await _cache.GetOrCreateAsync(
                key,
                _ =>
                {
                    factoryCalled = true;
                    return ValueTask.FromResult(CACHE_MISS_SENTINEL);
                },
                _cacheOptions,
                cancellationToken: cancellationToken);

            if (!factoryCalled && cached >= 0)
            {
                result[id] = cached;
            }
            else
            {
                if (!factoryCalled)
                {
                    // Sentinel оказался в кэше (теоретически возможно при гонке записи) —
                    // удаляем запись, чтобы не отдавать наружу значение из служебного диапазона.
                    await _cache.RemoveAsync(key, cancellationToken);
                }

                missIds.Add(id);
            }
        }

        if (missIds.Count == 0)
        {
            return Result.Success<IReadOnlyDictionary<Guid, long>, Error>(result);
        }

        Result<IReadOnlyDictionary<Guid, long>, Error> fetchResult =
            await _inner.GetMaterialViewsCountsAsync(missIds, cancellationToken);

        if (fetchResult.IsFailure)
        {
            return fetchResult.Error;
        }

        // Кэшируем КАЖДЫЙ id из missIds — включая нули (когда у материала нет просмотров).
        // Иначе для популярных «пустых» материалов будем гонять PG каждый раз.
        foreach (Guid id in missIds)
        {
            long count = fetchResult.Value.TryGetValue(id, out long c) ? c : 0L;
            result[id] = count;

            string key = $"{MATERIAL_VIEWS_COUNT_CACHE_KEY_PREFIX}{id}";
            await _cache.SetAsync(key, count, _cacheOptions, cancellationToken: cancellationToken);
        }

        return Result.Success<IReadOnlyDictionary<Guid, long>, Error>(result);
    }
}
