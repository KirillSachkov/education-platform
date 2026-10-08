using Ordering;

namespace EducationContentService.Domain.Courses;

/// <summary>
///     Join-сущность для связи курса с модулем или проектом.
///     Содержит позицию (SortKey) и флаг опциональности.
/// </summary>
public sealed class CourseItem : IOrderedItem
{
    public CourseItem(
        Guid courseId,
        CourseItemType itemType,
        Guid referenceId,
        SortKey sortKey,
        bool isOptional)
    {
        Id = Guid.CreateVersion7();
        CourseId = courseId;
        ItemType = itemType;
        ReferenceId = referenceId;
        SortKey = sortKey;
        IsOptional = isOptional;
    }

    // EF Core
    private CourseItem()
    {
    }

    public Guid Id { get; }

    public Guid CourseId { get; }

    public CourseItemType ItemType { get; }

    public Guid ReferenceId { get; }

    public SortKey SortKey { get; private set; } = null!;

    public bool IsOptional { get; private set; }

    public void UpdateSortKey(SortKey sortKey) => SortKey = sortKey;

    public void UpdateIsOptional(bool isOptional) => IsOptional = isOptional;
}
