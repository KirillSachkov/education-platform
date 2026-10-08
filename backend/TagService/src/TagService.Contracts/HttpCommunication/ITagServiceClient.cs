using TagService.Contracts.SearchLookup;

namespace TagService.Contracts.HttpCommunication;

public interface ITagServiceClient
{
    Task<Result<IReadOnlyList<EntityTagsSearchLookupBatchDto>, Error>> GetEntitiesTagsSearchLookupAsync(
        IReadOnlyList<EntityTagsSearchLookupBatchItem> entities,
        CancellationToken cancellationToken);
}
