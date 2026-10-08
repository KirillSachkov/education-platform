using System.Collections.Concurrent;
using ProgressService.Core.Database;

namespace ProgressService.IntegrationTests.Infrastructure;

/// <summary>
///     Outbox replacement for integration tests. External transports and Wolverine
///     persistence are disabled in tests, so real outbox delivery does not happen.
///     Instead this service records every published message so tests can assert
///     that a handler enqueued the expected cascade message.
///
///     Messages are NOT delivered automatically — tests that want to verify the
///     cascaded handler's behavior should manually invoke the downstream message
///     via <c>InvokeMessageAndWaitAsync</c> after the parent handler finishes.
/// </summary>
internal sealed class NoOpOutboxService : IOutboxService
{
    private static readonly ConcurrentQueue<object> _published = new();

    public static IReadOnlyCollection<object> Published => _published.ToArray();

    public static void Reset() => _published.Clear();

    public Task PublishAsync<T>(T message) where T : class
    {
        _published.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task FlushAsync() => Task.CompletedTask;
}
