using Common;

namespace TagService.Contracts.SearchLookup;

public sealed record GetEntityTagsSearchLookupRequest(
    EntityType EntityType,
    IReadOnlyList<Guid> EntityIds);
