using AuthService.Core.Database;
using Wolverine.Testing;

namespace AuthService.IntegrationTests.Infrastructure;

internal sealed class TestOutboxBuffer
{
    private readonly List<object> _messages = [];

    public void Add(object message) => _messages.Add(message);

    public void FlushTo(TestOutboxCollector collector)
    {
        foreach (object message in _messages)
            collector.Add(message);

        _messages.Clear();
    }
}

internal sealed class TestOutboxService(TestOutboxBuffer buffer) : IOutboxService
{
    public Task PublishAsync<T>(T message) where T : class
    {
        buffer.Add(message);
        return Task.CompletedTask;
    }
}
