using System.Collections.Concurrent;

namespace Shared.GitHubApp;

/// <summary>
///     In-memory store. GC-проход expired'ов на каждом ConsumeAsync — для типичного
///     install-flow это <c>O(active install attempts)</c>, обычно &lt; 100.
///
///     Для multi-instance prod — используйте <see cref="RedisInstallStateStore{TData}"/>.
///     В DI обычно регистрируется условно: если есть <c>IConnectionMultiplexer</c> —
///     Redis impl, иначе fallback на in-memory.
/// </summary>
public sealed class InMemoryInstallStateStore<TData> : IInstallStateStore<TData>
    where TData : class
{
    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public InMemoryInstallStateStore(TimeProvider time) => _time = time;

    public Task SetAsync(string stateToken, TData data, TimeSpan ttl)
    {
        _store[stateToken] = new Entry(data, _time.GetUtcNow() + ttl);
        return Task.CompletedTask;
    }

    public Task<TData?> ConsumeAsync(string stateToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        SweepExpired(now);
        if (_store.TryRemove(stateToken, out Entry? entry) && entry.ExpiresAt > now)
        {
            return Task.FromResult<TData?>(entry.Data);
        }

        return Task.FromResult<TData?>(null);
    }

    private void SweepExpired(DateTimeOffset now)
    {
        foreach (KeyValuePair<string, Entry> kv in _store)
        {
            if (kv.Value.ExpiresAt <= now)
            {
                _store.TryRemove(kv.Key, out _);
            }
        }
    }

    private sealed record Entry(TData Data, DateTimeOffset ExpiresAt);
}
