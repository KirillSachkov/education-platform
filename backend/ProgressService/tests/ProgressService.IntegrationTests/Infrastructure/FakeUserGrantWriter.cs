using System.Collections.Concurrent;
using ContentAccess;

namespace ProgressService.IntegrationTests.Infrastructure;

/// <summary>
///     Records GrantAsync / RevokeAsync calls so tests can assert Redis writes without
///     standing up a real Redis container. Thread-safe for parallel handler execution.
/// </summary>
public sealed class FakeUserGrantWriter : IUserGrantWriter
{
    private readonly ConcurrentBag<(Guid UserId, string Tag)> _granted = [];
    private readonly ConcurrentBag<(Guid UserId, string Tag)> _revoked = [];

    public IReadOnlyCollection<(Guid UserId, string Tag)> Granted => _granted;
    public IReadOnlyCollection<(Guid UserId, string Tag)> Revoked => _revoked;

    public void Reset()
    {
        _granted.Clear();
        _revoked.Clear();
    }

    public Task GrantAsync(Guid userId, string tag, CancellationToken ct = default)
    {
        _granted.Add((userId, tag));
        return Task.CompletedTask;
    }

    public Task GrantManyAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        foreach (string tag in tags)
            _granted.Add((userId, tag));
        return Task.CompletedTask;
    }

    public Task RevokeAsync(Guid userId, string tag, CancellationToken ct = default)
    {
        _revoked.Add((userId, tag));
        return Task.CompletedTask;
    }

    public Task RevokeManyAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        foreach (string tag in tags)
            _revoked.Add((userId, tag));
        return Task.CompletedTask;
    }

    public Task ReplaceAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        // ProgressService не использует ReplaceAsync напрямую (он только пишет/снимает теги).
        // Stub для compatibility интерфейса. Если когда-нибудь вызовут — записываем как
        // полный grant новых тегов.
        foreach (string tag in tags)
            _granted.Add((userId, tag));
        return Task.CompletedTask;
    }
}
