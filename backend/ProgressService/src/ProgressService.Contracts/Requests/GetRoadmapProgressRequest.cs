namespace ProgressService.Contracts.Requests;

public sealed record GetRoadmapProgressRequest(
    IReadOnlyCollection<RoadmapProgressItemRequest> Items);

public sealed record RoadmapProgressItemRequest(
    string EntityType,
    Guid EntityId,
    Guid? CourseId);
