using Common;

namespace SearchService.Contracts;

public sealed record SearchReindexResponse(
    EntityType? EntityType,
    int ProcessedDocuments,
    int Batches,
    IReadOnlyList<SearchReindexEntityResult> Entities,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc);
