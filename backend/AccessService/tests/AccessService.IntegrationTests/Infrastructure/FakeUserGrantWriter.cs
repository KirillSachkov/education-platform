using System.Collections.Concurrent;
using ContentAccess;

namespace AccessService.IntegrationTests.Infrastructure;

/// <summary>
///     Records GrantAsync / RevokeAsync calls so tests can assert Redis writes without
///     standing up a real Redis container.
/// </summary>
public sealed class FakeUserGrantWriter : IUserGrantWriter
{
    private readonly ConcurrentBag<(Guid UserId, string Tag)> _granted = [];
    private readonly ConcurrentBag<(Guid UserId, string Tag)> _revoked = [];
    private readonly ConcurrentDictionary<Guid, HashSet<string>> _state = new();
    private readonly System.Threading.Lock _stateLock = new();

    public IReadOnlyCollection<(Guid UserId, string Tag)> Granted => _granted;
    public IReadOnlyCollection<(Guid UserId, string Tag)> Revoked => _revoked;

    /// <summary>Snapshot текущих тегов пользователя (отражает Redis state).</summary>
    public IReadOnlySet<string> Snapshot(Guid userId) =>
        _state.TryGetValue(userId, out HashSet<string>? set) ? [.. set] : new HashSet<string>();

    public void Reset()
    {
        _granted.Clear();
        _revoked.Clear();
        _state.Clear();
    }

    public Task GrantAsync(Guid userId, string tag, CancellationToken ct = default)
    {
        _granted.Add((userId, tag));
        lock (_stateLock)
            _state.GetOrAdd(userId, _ => []).Add(tag);
        return Task.CompletedTask;
    }

    public Task GrantManyAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        foreach (string tag in tags)
            _granted.Add((userId, tag));
        lock (_stateLock)
        {
            HashSet<string> set = _state.GetOrAdd(userId, _ => []);
            foreach (string tag in tags)
                set.Add(tag);
        }
        return Task.CompletedTask;
    }

    public Task RevokeAsync(Guid userId, string tag, CancellationToken ct = default)
    {
        _revoked.Add((userId, tag));
        lock (_stateLock)
        {
            if (_state.TryGetValue(userId, out HashSet<string>? set))
                set.Remove(tag);
        }
        return Task.CompletedTask;
    }

    public Task RevokeManyAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        foreach (string tag in tags)
            _revoked.Add((userId, tag));
        lock (_stateLock)
        {
            if (_state.TryGetValue(userId, out HashSet<string>? set))
                foreach (string tag in tags)
                    set.Remove(tag);
        }
        return Task.CompletedTask;
    }

    public Task ReplaceAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            HashSet<string>? oldSet = _state.TryGetValue(userId, out HashSet<string>? existing) ? existing : null;
            HashSet<string> newSet = [.. tags];

            // Track diff as Granted/Revoked для test-наблюдаемости.
            if (oldSet is not null)
            {
                foreach (string removed in oldSet.Except(newSet))
                    _revoked.Add((userId, removed));
                foreach (string added in newSet.Except(oldSet))
                    _granted.Add((userId, added));
            }
            else
            {
                foreach (string added in newSet)
                    _granted.Add((userId, added));
            }

            _state[userId] = newSet;
        }
        return Task.CompletedTask;
    }
}
