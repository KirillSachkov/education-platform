namespace TagService.Contracts.SearchLookup;

public sealed record GetEntitiesTagsSearchLookupRequest(
    IReadOnlyList<EntityTagsSearchLookupBatchItem> Entities);
