using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Domain.Roadmaps;

public sealed class Roadmap
{
    private readonly List<RoadmapNode> _nodes = [];
    private readonly List<RoadmapEdge> _edges = [];

    public Roadmap(
        Guid authorId,
        Title title,
        Description? description,
        Guid? courseId,
        string? slug)
    {
        Id = Guid.CreateVersion7();
        AuthorId = authorId;
        Title = title;
        Description = description;
        CourseId = courseId;
        Slug = slug;
        Status = PublicationStatus.DRAFT;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    private Roadmap() { }

    public Guid Id { get; }
    public Guid AuthorId { get; }
    public Title Title { get; private set; } = null!;
    public Description? Description { get; private set; }
    public Guid? CourseId { get; }
    public string? Slug { get; private set; }
    public PublicationStatus Status { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyList<RoadmapNode> Nodes => _nodes;
    public IReadOnlyList<RoadmapEdge> Edges => _edges;

    public void Update(Title title, Description? description, string? slug)
    {
        Title = title;
        Description = description;
        Slug = slug;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ReplaceCanvas(IReadOnlyList<RoadmapNode> nodes, IReadOnlyList<RoadmapEdge> edges)
    {
        _nodes.Clear();
        _edges.Clear();

        _nodes.AddRange(nodes);
        _edges.AddRange(edges);

        UpdatedAt = DateTime.UtcNow;
    }

    public UnitResult<Error> Publish()
    {
        if (Status != PublicationStatus.DRAFT)
            return EducationErrors.InvalidStatusTransition(Status.ToString(), nameof(PublicationStatus.PUBLISHED));

        Status = PublicationStatus.PUBLISHED;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Archive()
    {
        if (Status != PublicationStatus.PUBLISHED)
            return EducationErrors.InvalidStatusTransition(Status.ToString(), nameof(PublicationStatus.ARCHIVED));

        Status = PublicationStatus.ARCHIVED;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Restore()
    {
        if (Status != PublicationStatus.ARCHIVED)
            return EducationErrors.InvalidStatusTransition(Status.ToString(), nameof(PublicationStatus.PUBLISHED));

        Status = PublicationStatus.PUBLISHED;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }
}
