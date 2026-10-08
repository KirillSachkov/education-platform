namespace SearchService.Core.Database;

public interface IOutboxService
{
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class;
}
