namespace SearchService.Core.Reindex;

public interface ISearchIndexingConsumerController
{
    Task<Result<IAsyncDisposable, Error>> PauseAsync(CancellationToken cancellationToken = default);
}
