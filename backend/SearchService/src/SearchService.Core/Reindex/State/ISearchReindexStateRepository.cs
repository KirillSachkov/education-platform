namespace SearchService.Core.Reindex.State;

public interface ISearchReindexStateRepository
{
    Task<SearchReindexState> GetOrInitAsync(CancellationToken cancellationToken = default);

    Task MarkAppliedAsync(
        int generation,
        string? schemaHash,
        string? deployStamp,
        Guid requestId,
        DateTime appliedAtUtc,
        CancellationToken cancellationToken = default);
}
