using Common;

namespace TagService.Contracts.SearchLookup;

public sealed record EntityTagsSearchLookupBatchItem(
    EntityType EntityType,
    Guid EntityId);
