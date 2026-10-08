namespace EducationContentService.Contracts.Roadmaps;

public sealed record SaveCanvasRequest(
    IReadOnlyList<SaveCanvasNodeDto> Nodes,
    IReadOnlyList<SaveCanvasEdgeDto> Edges);

public sealed record SaveCanvasNodeDto(
    Guid Id,
    string NodeType,
    double PositionX,
    double PositionY,
    double? Width,
    double? Height,
    Guid? ParentNodeId,
    string Data,
    int SortOrder);

public sealed record SaveCanvasEdgeDto(
    Guid Id,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string? Label,
    string EdgeType,
    bool Animated,
    string? SourceHandle,
    string? TargetHandle);
