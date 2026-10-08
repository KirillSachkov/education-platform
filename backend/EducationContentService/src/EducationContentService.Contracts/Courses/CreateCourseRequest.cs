namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Запрос на создание курса.
/// </summary>
/// <param name="Kind">
///     Тип курса: <c>"COURSE"</c> (по умолчанию) либо <c>"INTENSIVE"</c>. Регистр приводится к
///     UPPER_SNAKE_CASE на бэке. Интенсивы — мини-курсы без issues; см. <c>Course.Kind</c>.
/// </param>
public sealed record CreateCourseRequest(
    string Title,
    string Description,
    string Slug,
    string? Kind = null);
