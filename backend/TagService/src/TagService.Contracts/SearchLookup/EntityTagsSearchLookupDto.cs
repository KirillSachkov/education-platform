namespace TagService.Contracts.SearchLookup;

public sealed record EntityTagsSearchLookupDto(
    Guid EntityId,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<string> TagTitles);
