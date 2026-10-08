namespace SearchService.Core.Features.Reindex.IntegrationEvents;

public sealed record ProjectsSearchReindexRequested(Guid RequestId, DateTime RequestedAtUtc);
