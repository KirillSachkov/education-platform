using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TrainerService.Core.Features.Sessions.Grading;

/// <summary>
///     Singleton-backed bounded, de-duplicated channel of session ids awaiting AI grading (#585). Producer side
///     is <see cref="Enqueue"/> (called from CompleteSession + the startup-recovery sweep); the
///     consumer is <see cref="MockGradingBackgroundService"/>, which reads <see cref="Reader"/>.
/// </summary>
public sealed class MockGradingQueue : IMockGradingQueue
{
    private const int CAPACITY = 1_024;

    private readonly ConcurrentDictionary<Guid, byte> _queued = new();
    private readonly Channel<Guid> _channel =
        Channel.CreateBounded<Guid>(new BoundedChannelOptions(CAPACITY)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Enqueue(Guid sessionId)
    {
        if (!_queued.TryAdd(sessionId, 0))
            return;

        // A full channel is safe: the PENDING row remains the source of truth and the periodic DB
        // recovery sweep retries it. Remove the de-dup marker so that retry can enqueue it later.
        if (!_channel.Writer.TryWrite(sessionId))
            _queued.TryRemove(sessionId, out _);
    }

    public void MarkDequeued(Guid sessionId) => _queued.TryRemove(sessionId, out _);
}
