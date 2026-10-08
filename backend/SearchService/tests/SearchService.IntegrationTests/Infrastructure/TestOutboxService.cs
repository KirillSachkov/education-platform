using SearchService.Core.Database;
using Wolverine.Testing;

namespace SearchService.IntegrationTests.Infrastructure;

/// <summary>
///     Per-service shim над <see cref="TestOutboxCollector"/>: реализует локальный
///     <see cref="IOutboxService"/> и делегирует publish'и в shared collector.
///     Тесты ассертят публикации через <c>OutboxCollector.OfType&lt;T&gt;()</c>.
/// </summary>
internal sealed class TestOutboxService(TestOutboxCollector collector) : IOutboxService
{
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        collector.Add(message);
        return Task.CompletedTask;
    }
}
