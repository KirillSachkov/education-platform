namespace EducationContentService.Contracts.Roadmaps;

public sealed record RoadmapSummaryDto(
    Guid Id,
    Guid AuthorId,
    string Title,
    string? Description,
    Guid? CourseId,
    string? Slug,
    string Status,
    int NodeCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);
