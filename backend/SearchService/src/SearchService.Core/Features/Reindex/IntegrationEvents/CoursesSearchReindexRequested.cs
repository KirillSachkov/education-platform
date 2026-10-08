namespace SearchService.Core.Features.Reindex.IntegrationEvents;

public sealed record CoursesSearchReindexRequested(Guid RequestId, DateTime RequestedAtUtc);
