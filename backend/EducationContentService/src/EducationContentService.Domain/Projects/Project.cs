using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Domain.Projects;

/// <summary>
///     Aggregate Root — практический контейнер.
///     Содержит упорядоченный список ProjectItem, каждый из которых ссылается на Issue.
/// </summary>
public sealed class Project
{
    public Project(
        Guid authorId,
        Title title,
        Description? description = null,
        DetailedDescription? detailedDescription = null)
    {
        Id = Guid.CreateVersion7();
        AuthorId = authorId;
        Title = title;
        Description = description;
        DetailedDescription = detailedDescription;
        Status = PublicationStatus.DRAFT;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    // EF Core
    private Project()
    {
    }

    public Guid Id { get; }

    public Guid AuthorId { get; }

    public Title Title { get; private set; } = null!;

    public Description? Description { get; private set; }

    public DetailedDescription? DetailedDescription { get; private set; }

    public PublicationStatus Status { get; private set; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public void Update(Title title, Description? description = null, DetailedDescription? detailedDescription = null)
    {
        Title = title;
        Description = description;
        DetailedDescription = detailedDescription;
        UpdatedAt = DateTime.UtcNow;
    }

    public UnitResult<Error> Publish()
    {
        if (Status != PublicationStatus.DRAFT)
            return EducationErrors.InvalidProjectStatusTransition(Status.ToString(), nameof(PublicationStatus.PUBLISHED));

        Status = PublicationStatus.PUBLISHED;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Archive()
    {
        if (Status != PublicationStatus.PUBLISHED)
            return EducationErrors.InvalidProjectStatusTransition(Status.ToString(), nameof(PublicationStatus.ARCHIVED));

        Status = PublicationStatus.ARCHIVED;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Restore()
    {
        if (Status != PublicationStatus.ARCHIVED)
            return EducationErrors.InvalidProjectStatusTransition(Status.ToString(), nameof(PublicationStatus.PUBLISHED));

        Status = PublicationStatus.PUBLISHED;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }
}
