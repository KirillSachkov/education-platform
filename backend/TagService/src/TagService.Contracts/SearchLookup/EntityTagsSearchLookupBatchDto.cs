using Common;

namespace TagService.Contracts.SearchLookup;

public sealed record EntityTagsSearchLookupBatchDto(
    EntityType EntityType,
    Guid EntityId,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<string> TagTitles);
