namespace EducationContentService.Contracts.Roadmaps;

public sealed record CreateRoadmapRequest(
    string Title,
    string? Description,
    Guid? CourseId,
    string? Slug);
