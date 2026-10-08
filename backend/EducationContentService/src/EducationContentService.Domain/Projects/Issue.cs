using EducationContentService.Domain.Projects.ValueObjects;
using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Domain.Projects;

/// <summary>
///     Aggregate Root — практическая задача.
///     Инвариант A3: жёстко привязана к проекту (ProjectId обязателен).
/// </summary>
public sealed class Issue
{
    private List<IssueExternalLink> _externalLinks = [];
    private List<IssueInternalMaterial> _internalMaterials = [];

    /// <summary>
    ///     Issue не может существовать без Project.
    /// </summary>
    public Issue(
        Guid authorId,
        Guid projectId,
        Title title,
        MarkdownContent content,
        AccessType accessType = AccessType.ENROLLED,
        IssueSubmissionMode submissionMode = IssueSubmissionMode.PULL_REQUEST,
        string? selfCheckInstructions = null)
    {
        Id = Guid.CreateVersion7();
        AuthorId = authorId;
        ProjectId = projectId;
        Title = title;
        Content = content;
        Status = PublicationStatus.DRAFT;
        AccessType = accessType;
        SubmissionMode = submissionMode;
        SelfCheckInstructions = NormalizeSelfCheckInstructions(selfCheckInstructions);
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    // EF Core
    private Issue()
    {
    }

    public Guid Id { get; }

    public Guid AuthorId { get; }

    public Guid ProjectId { get; }

    public Title Title { get; private set; } = null!;

    public MarkdownContent Content { get; private set; } = null!;

    public PublicationStatus Status { get; private set; }

    public AccessType AccessType { get; private set; }

    public IssueSubmissionMode SubmissionMode { get; private set; }

    public string? SelfCheckInstructions { get; private set; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyList<IssueExternalLink> ExternalLinks => _externalLinks.AsReadOnly();

    public IReadOnlyList<IssueInternalMaterial> InternalMaterials => _internalMaterials.AsReadOnly();

    public void Update(
        Title title,
        MarkdownContent content,
        AccessType accessType,
        IssueSubmissionMode submissionMode = IssueSubmissionMode.PULL_REQUEST,
        string? selfCheckInstructions = null)
    {
        Title = title;
        Content = content;
        AccessType = accessType;
        SubmissionMode = submissionMode;
        SelfCheckInstructions = NormalizeSelfCheckInstructions(selfCheckInstructions);
        UpdatedAt = DateTime.UtcNow;
    }

    private static string? NormalizeSelfCheckInstructions(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void UpdateExternalLinks(IReadOnlyList<IssueExternalLink> externalLinks)
    {
        _externalLinks = [.. externalLinks];
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateInternalMaterials(IReadOnlyList<IssueInternalMaterial> internalMaterials)
    {
        _internalMaterials = [.. internalMaterials];
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
