namespace EducationContentService.Domain.Roadmaps;

public sealed class RoadmapEdge
{
    public RoadmapEdge(
        Guid id,
        Guid roadmapId,
        Guid sourceNodeId,
        Guid targetNodeId,
        string? label,
        string edgeType,
        bool animated,
        string? sourceHandle,
        string? targetHandle)
    {
        Id = id;
        RoadmapId = roadmapId;
        SourceNodeId = sourceNodeId;
        TargetNodeId = targetNodeId;
        Label = label;
        EdgeType = edgeType;
        Animated = animated;
        SourceHandle = sourceHandle;
        TargetHandle = targetHandle;
    }

    private RoadmapEdge() { }

    public Guid Id { get; }
    public Guid RoadmapId { get; }
    public Guid SourceNodeId { get; }
    public Guid TargetNodeId { get; }
    public string? Label { get; private set; }
    public string EdgeType { get; private set; } = null!;
    public bool Animated { get; private set; }
    public string? SourceHandle { get; private set; }
    public string? TargetHandle { get; private set; }
}
