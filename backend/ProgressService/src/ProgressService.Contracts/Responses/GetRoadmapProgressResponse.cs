namespace ProgressService.Contracts.Responses;

public sealed record GetRoadmapProgressResponse(
    IReadOnlyCollection<RoadmapProgressItemDto> Items,
    IReadOnlyCollection<Guid> EnrolledCourseIds);

public sealed record RoadmapProgressItemDto(
    string EntityType,
    Guid EntityId,
    Guid? CourseId,
    string Status,
    DateTime? CompletedAt);
