using TrainerService.Core.Features.Sessions.Grading;

namespace TrainerService.IntegrationTests.Features.Sessions;

public sealed class MockGradingQueueTests
{
    [Fact]
    public void Queue_deduplicates_until_the_session_is_dequeued()
    {
        var queue = new MockGradingQueue();
        Guid sessionId = Guid.CreateVersion7();

        queue.Enqueue(sessionId);
        queue.Enqueue(sessionId);

        Assert.True(queue.Reader.TryRead(out Guid queued));
        Assert.Equal(sessionId, queued);
        Assert.False(queue.Reader.TryRead(out _));

        queue.MarkDequeued(sessionId);
        queue.Enqueue(sessionId);

        Assert.True(queue.Reader.TryRead(out queued));
        Assert.Equal(sessionId, queued);
    }
}
