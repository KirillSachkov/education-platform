namespace SearchService.Core.Features.Reindex.IntegrationEvents;

public sealed record IssuesSearchReindexRequested(Guid RequestId, DateTime RequestedAtUtc);
