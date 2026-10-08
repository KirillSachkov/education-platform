using Ordering;

namespace EducationContentService.Domain.Courses;

/// <summary>
///     Join-сущность для связи курса с квизом (зеркало <see cref="CourseMaterial"/>, #489).
///     Источник правды о принадлежности квиза курсу — питает access-теги ENROLLED-квизов
///     (ST-11) и размещение квиза в модулях (ST-12, по образцу INV-4 материалов).
/// </summary>
public sealed class CourseQuiz
{
    public CourseQuiz(Guid courseId, Guid quizId, SortKey sortKey)
    {
        Id = Guid.CreateVersion7();
        CourseId = courseId;
        QuizId = quizId;
        SortKey = sortKey;
    }

    // EF Core
    private CourseQuiz()
    {
    }

    public Guid Id { get; }

    public Guid CourseId { get; }

    public Guid QuizId { get; }

    public SortKey SortKey { get; private set; } = null!;

    public void UpdateSortKey(SortKey sortKey) => SortKey = sortKey;
}
