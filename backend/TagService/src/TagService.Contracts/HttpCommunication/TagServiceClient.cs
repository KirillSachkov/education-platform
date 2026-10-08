using Core.HttpCommunication;
using Microsoft.Extensions.Logging;
using TagService.Contracts.SearchLookup;

namespace TagService.Contracts.HttpCommunication;

internal sealed class TagServiceClient : BaseHttpClient, ITagServiceClient
{
    private const string SERVICE_NAME = "TagService";

    public TagServiceClient(
        HttpClient httpClient,
        ILogger<TagServiceClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public Task<Result<IReadOnlyList<EntityTagsSearchLookupBatchDto>, Error>> GetEntitiesTagsSearchLookupAsync(
        IReadOnlyList<EntityTagsSearchLookupBatchItem> entities,
        CancellationToken cancellationToken)
        => PostAsync<GetEntitiesTagsSearchLookupRequest, IReadOnlyList<EntityTagsSearchLookupBatchDto>>(
            "/internal/search/entities-tags/batch",
            new GetEntitiesTagsSearchLookupRequest(entities),
            cancellationToken);
}
