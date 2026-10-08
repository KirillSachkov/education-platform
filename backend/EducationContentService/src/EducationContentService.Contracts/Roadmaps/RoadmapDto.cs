namespace EducationContentService.Contracts.Roadmaps;

public sealed record RoadmapDto(
    Guid Id,
    Guid AuthorId,
    string Title,
    string? Description,
    Guid? CourseId,
    string? Slug,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<RoadmapNodeDto> Nodes,
    IReadOnlyList<RoadmapEdgeDto> Edges);

public sealed record RoadmapNodeDto(
    Guid Id,
    string NodeType,
    double PositionX,
    double PositionY,
    double? Width,
    double? Height,
    Guid? ParentNodeId,
    string Data,
    int SortOrder);

public sealed record RoadmapEdgeDto(
    Guid Id,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string? Label,
    string EdgeType,
    bool Animated,
    string? SourceHandle,
    string? TargetHandle);
