using Ordering;

namespace EducationContentService.Domain.Courses;

/// <summary>
///     Join-сущность для связи курса с дополнительным материалом.
///     Содержит позицию материала в библиотеке материалов курса.
/// </summary>
public sealed class CourseMaterial
{
    public CourseMaterial(Guid courseId, Guid materialId, SortKey sortKey)
    {
        Id = Guid.CreateVersion7();
        CourseId = courseId;
        MaterialId = materialId;
        SortKey = sortKey;
    }

    // EF Core
    private CourseMaterial()
    {
    }

    public Guid Id { get; }

    public Guid CourseId { get; }

    public Guid MaterialId { get; }

    public SortKey SortKey { get; private set; } = null!;

    public void UpdateSortKey(SortKey sortKey) => SortKey = sortKey;
}
