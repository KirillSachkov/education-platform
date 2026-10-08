namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Список мест, в которых используется материал: курсы, модули, подборки.
///     Нужен для UI «этот материал также находится в …» на странице материала
///     и для страницы автора (чтобы видеть где материал используется перед удалением).
/// </summary>
public sealed record MaterialBindingsDto(
    Guid MaterialId,
    IReadOnlyList<MaterialCourseBindingDto> Courses,
    IReadOnlyList<MaterialModuleBindingDto> Modules,
    IReadOnlyList<MaterialCollectionBindingDto> Collections);

public sealed record MaterialCourseBindingDto(
    Guid CourseId,
    string Title,
    string Slug,
    Guid AuthorId);

public sealed record MaterialModuleBindingDto(
    Guid ModuleId,
    string Title,
    Guid CourseId,
    string CourseTitle,
    string CourseSlug);

public sealed record MaterialCollectionBindingDto(
    Guid CollectionId,
    string Title,
    Guid? CourseId,
    string? CourseSlug);
