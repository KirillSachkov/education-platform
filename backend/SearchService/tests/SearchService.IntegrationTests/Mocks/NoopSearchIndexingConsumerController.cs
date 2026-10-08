using CSharpFunctionalExtensions;
using SearchService.Core.Reindex;
using SharedKernel;

namespace SearchService.IntegrationTests.Mocks;

public sealed class NoopSearchIndexingConsumerController : ISearchIndexingConsumerController
{
    private int _pauseCalls;
    private int _resumeCalls;
    private int _failNextResume;

    public int PauseCalls => Volatile.Read(ref _pauseCalls);
    public int ResumeCalls => Volatile.Read(ref _resumeCalls);

    public void FailNextResume() => Interlocked.Exchange(ref _failNextResume, 1);

    public Task<Result<IAsyncDisposable, Error>> PauseAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _pauseCalls);
        return Task.FromResult(Result.Success<IAsyncDisposable, Error>(new NoopScope(this)));
    }

    private sealed class NoopScope : IAsyncDisposable
    {
        private readonly NoopSearchIndexingConsumerController _owner;

        public NoopScope(NoopSearchIndexingConsumerController owner)
        {
            _owner = owner;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _owner._resumeCalls);
            if (Interlocked.Exchange(ref _owner._failNextResume, 0) == 1)
            {
                return ValueTask.FromException(new InvalidOperationException("resume failed"));
            }

            return ValueTask.CompletedTask;
        }
    }
}
