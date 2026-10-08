using Common;

namespace SearchService.Contracts;

public sealed record SearchReindexEnqueuedResponse(
    Guid RequestId,
    EntityType? EntityType,
    DateTime RequestedAtUtc);
