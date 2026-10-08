namespace SearchService.Core.Features.Reindex.IntegrationEvents;

public sealed record ModulesSearchReindexRequested(Guid RequestId, DateTime RequestedAtUtc);
