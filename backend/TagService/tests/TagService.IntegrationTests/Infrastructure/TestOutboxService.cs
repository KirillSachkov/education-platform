using TagService.Core.Database;
using Wolverine.Testing;

namespace TagService.IntegrationTests.Infrastructure;

internal sealed class TestOutboxService(TestOutboxCollector collector) : IOutboxService
{
    public Task PublishAsync<T>(T message) where T : class
    {
        collector.Add(message);
        return Task.CompletedTask;
    }

    public Task FlushAsync() => Task.CompletedTask;
}
