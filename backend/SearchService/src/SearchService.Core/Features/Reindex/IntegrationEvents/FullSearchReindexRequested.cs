namespace SearchService.Core.Features.Reindex.IntegrationEvents;

public sealed record FullSearchReindexRequested(Guid RequestId, DateTime RequestedAtUtc);
