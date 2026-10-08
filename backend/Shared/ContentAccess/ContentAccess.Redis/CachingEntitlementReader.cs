using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;

namespace ContentAccess.Redis;

/// <summary>
/// Декоратор над <see cref="RedisEntitlementReader"/>, кеширующий результат
/// <c>GetUserGrantTagsAsync</c> в локальном <see cref="IMemoryCache"/> на короткое
/// окно (default 3s). Режет SMEMBERS при burst-ах (consecutive /search + /feed + /detail
/// от одного юзера за секунду — один Redis-хит вместо трёх).
///
/// Свежесть: grants меняются только при enrollment/revocation — редкие события.
/// 3s stale — приемлемо: worst case пользователь увидит «locked» на 3 секунды дольше
/// после самозаписи. Invalidate не нужна; TTL решает задачу.
/// </summary>
public sealed class CachingEntitlementReader : IEntitlementReader
{
    private static readonly TimeSpan TTL = TimeSpan.FromSeconds(3);
    private const string CACHE_KEY_PREFIX = "contentaccess:grants:";

    private readonly RedisEntitlementReader _inner;
    private readonly IMemoryCache _cache;

    public CachingEntitlementReader(RedisEntitlementReader inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<EntitlementGrantSet> GetUserGrantTagsAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return EntitlementGrantSet.Empty;
        }

        string cacheKey = CACHE_KEY_PREFIX + userId.ToString("D");

        if (_cache.TryGetValue(cacheKey, out EntitlementGrantSet? cached) && cached is not null)
        {
            Activity.Current?.SetTag("contentaccess.grants.cache", "hit");
            return cached;
        }

        EntitlementGrantSet fresh = await _inner.GetUserGrantTagsAsync(userId, ct);
        _cache.Set(cacheKey, fresh, TTL);
        Activity.Current?.SetTag("contentaccess.grants.cache", "miss");
        return fresh;
    }
}
