namespace EducationContentService.Contracts.Collections;

/// <summary>
///     <paramref name="AccessType"/> — опциональное поле.
///     Если не задано, выбирается дефолт: без <paramref name="CourseId"/> → PUBLIC, иначе → ENROLLED.
///     Допустимые значения: <c>PUBLIC</c>, <c>REGISTERED</c>, <c>FREE</c>, <c>ENROLLED</c>.
///     Для space-level подборки (<paramref name="CourseId"/> = null) допустимы только PUBLIC/REGISTERED.
/// </summary>
public sealed record CreateCollectionRequest(
    string Title,
    string? Description,
    Guid? CourseId,
    string? AccessType = null);
