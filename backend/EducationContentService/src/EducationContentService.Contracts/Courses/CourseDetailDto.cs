namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Полная информация о курсе с перечнем элементов.
/// </summary>
public sealed record CourseDetailDto(
    Guid Id,
    Guid AuthorId,
    string Slug,
    string Title,
    string Description,
    string Status,
    string Kind,
    Guid? ImageId,
    Guid? VideoId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<CourseItemDto> Items,
    // AuthorDisplayName / AuthorAvatarUrl — авторский кредит курса (#569, model A).
    // Обогащается на бэке через AuthService (display name) + FileService (avatar URL).
    string? AuthorDisplayName = null,
    string? AuthorAvatarUrl = null);

/// <summary>
///     Элемент курса (модуль или проект).
/// </summary>
public sealed record CourseItemDto(
    Guid Id,
    Guid ReferenceId,
    string ItemType,
    string SortKey,
    bool IsOptional,
    string? Title,
    string? Status);
