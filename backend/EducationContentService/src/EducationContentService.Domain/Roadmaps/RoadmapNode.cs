namespace EducationContentService.Domain.Roadmaps;

public sealed class RoadmapNode
{
    public RoadmapNode(
        Guid id,
        Guid roadmapId,
        RoadmapNodeType nodeType,
        double positionX,
        double positionY,
        double? width,
        double? height,
        Guid? parentNodeId,
        string data,
        int sortOrder)
    {
        Id = id;
        RoadmapId = roadmapId;
        NodeType = nodeType;
        PositionX = positionX;
        PositionY = positionY;
        Width = width;
        Height = height;
        ParentNodeId = parentNodeId;
        Data = data;
        SortOrder = sortOrder;
    }

    private RoadmapNode() { }

    public Guid Id { get; }
    public Guid RoadmapId { get; }
    public RoadmapNodeType NodeType { get; }
    public double PositionX { get; private set; }
    public double PositionY { get; private set; }
    public double? Width { get; private set; }
    public double? Height { get; private set; }
    public Guid? ParentNodeId { get; private set; }
    public string Data { get; private set; } = null!;
    public int SortOrder { get; private set; }

    public void UpdatePosition(double x, double y)
    {
        PositionX = x;
        PositionY = y;
    }

    public void UpdateSize(double? width, double? height)
    {
        Width = width;
        Height = height;
    }

    public void UpdateParent(Guid? parentNodeId) => ParentNodeId = parentNodeId;

    public void UpdateData(string data) => Data = data;

    public void UpdateSortOrder(int sortOrder) => SortOrder = sortOrder;
}
