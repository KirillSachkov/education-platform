using EducationContentService.Domain.Projects.ValueObjects;
using Ordering;

namespace EducationContentService.Domain.Projects;

/// <summary>
///     Join-сущность для связи проекта с задачей (Issue).
///     Содержит позицию (SortKey), флаг опциональности и максимальный балл.
/// </summary>
public sealed class ProjectItem : IOrderedItem
{
    public ProjectItem(
        Guid projectId,
        Guid issueId,
        SortKey sortKey,
        bool isOptional,
        MaxScore? maxScore)
    {
        Id = Guid.CreateVersion7();
        ProjectId = projectId;
        IssueId = issueId;
        SortKey = sortKey;
        IsOptional = isOptional;
        MaxScore = maxScore;
    }

    // EF Core
    private ProjectItem()
    {
    }

    public Guid Id { get; }

    public Guid ProjectId { get; }

    public Guid IssueId { get; }

    public SortKey SortKey { get; private set; } = null!;

    public bool IsOptional { get; private set; }

    public MaxScore? MaxScore { get; private set; }

    public void UpdateSortKey(SortKey sortKey) => SortKey = sortKey;

    public void UpdateIsOptional(bool isOptional) => IsOptional = isOptional;

    public void UpdateMaxScore(MaxScore? maxScore) => MaxScore = maxScore;
}
