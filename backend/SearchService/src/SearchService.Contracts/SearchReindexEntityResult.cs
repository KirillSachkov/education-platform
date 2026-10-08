using Common;

namespace SearchService.Contracts;

public sealed record SearchReindexEntityResult(
    EntityType EntityType,
    int ProcessedDocuments,
    int Batches,
    long TotalCount);
