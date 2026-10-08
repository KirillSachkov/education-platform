using TelegramBotService.Core.Database;
using Wolverine.Testing;

namespace TelegramBotService.IntegrationTests.Infrastructure;

/// <summary>
///     Per-service shim над <see cref="TestOutboxCollector"/>: реализует локальный
///     <see cref="IOutboxService"/> и делегирует publish'и в shared collector. Тесты
///     ассертят публикации через <c>OutboxCollector.OfType&lt;T&gt;()</c>.
/// </summary>
internal sealed class TestOutboxService : IOutboxService
{
    private readonly TestOutboxCollector _collector;

    public TestOutboxService(TestOutboxCollector collector) => _collector = collector;

    public Task PublishAsync<T>(T message) where T : class
    {
        _collector.Add(message);
        return Task.CompletedTask;
    }

    public Task FlushAsync() => Task.CompletedTask;
}
