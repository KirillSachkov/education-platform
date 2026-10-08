namespace EducationContentService.Contracts.Digest;

/// <summary>
///     Глобальный контент для еженедельного дайджеста (#532): опубликованные за окно
///     материалы и курсы. S2S-контракт для NotificationService — только метаданные
///     (title + route-context), без тел материалов.
/// </summary>
public sealed record DigestContentDto(
    IReadOnlyList<DigestMaterialDto> Materials,
    IReadOnlyList<DigestCourseDto> Courses);

/// <summary>
///     Опубликованный материал. <paramref name="CourseSlug"/> — primary-привязка
///     к PUBLISHED-курсу (самая ранняя <c>course_materials</c>); null — standalone (KB).
/// </summary>
public sealed record DigestMaterialDto(
    Guid MaterialId,
    string Title,
    string? CourseSlug,
    string? CourseTitle,
    DateTime PublishedAt);

public sealed record DigestCourseDto(
    Guid CourseId,
    string Title,
    string Slug,
    string Kind,
    DateTime PublishedAt);
