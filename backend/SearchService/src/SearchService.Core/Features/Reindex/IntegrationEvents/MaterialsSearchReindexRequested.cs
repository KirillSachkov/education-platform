namespace SearchService.Core.Features.Reindex.IntegrationEvents;

public sealed record MaterialsSearchReindexRequested(Guid RequestId, DateTime RequestedAtUtc);
