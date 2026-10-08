namespace EducationContentService.Contracts.Roadmaps;

public sealed record UpdateRoadmapRequest(
    string Title,
    string? Description,
    string? Slug);
