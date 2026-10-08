using ProgressService.Contracts.Dtos;

namespace ProgressService.Contracts.Responses;

public sealed record ReviewIssuesPagedResponse(
    IReadOnlyList<ReviewIssueItemDto> Items,
    string? NextCursor,
    // DEPRECATED: use cursor pagination (NextCursor)
    int TotalCount,
    // DEPRECATED: use cursor pagination (NextCursor)
    int Page,
    // DEPRECATED: use cursor pagination (NextCursor)
    int PageSize,
    // DEPRECATED: use cursor pagination (NextCursor)
    int TotalPages);
