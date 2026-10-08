namespace EducationContentService.Contracts.Collections;

public sealed record CollectionSummaryDto(
    Guid Id,
    Guid AuthorId,
    string Title,
    string? Description,
    string? CoverImageUrl,
    int ItemCount,
    Guid? CourseId,
    string? CourseTitle,
    string? CourseSlug,
    string Status,
    string AccessType,
    bool IsAccessible,
    string? LockReason,
    bool IsPinned,
    string? PinnedSortKey,
    DateTime CreatedAt,
    DateTime UpdatedAt);
