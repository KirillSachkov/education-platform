using EducationContentService.Contracts.Modules;

namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Полные данные курса для конструктора (авторская панель).
///     Включает все модули/проекты с вложенными элементами любого статуса.
/// </summary>
public sealed record CourseBuilderDto(
    Guid Id,
    Guid AuthorId,
    string Slug,
    string Title,
    string Description,
    string Status,
    string Kind,
    Guid? ImageId,
    Guid? VideoId,
    Guid? GettingStartedModuleId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsNew,
    IReadOnlyList<BuilderSectionDto> Sections,
    IReadOnlyList<string> LearningOutcomes,
    IReadOnlyList<string> TargetAudience,
    IReadOnlyList<string> Prerequisites);

/// <summary>
///     Секция конструктора (модуль или проект) с вложенными элементами.
/// </summary>
public sealed record BuilderSectionDto(
    Guid Id,
    string ItemType,
    string Title,
    string? Description,
    string? DetailedDescription,
    string Status,
    string SortKey,
    bool IsOptional,
    bool RequiresGithubConnection,
    bool RequiresReviewApp,
    bool IsAutoReviewEnabled,
    IReadOnlyList<ModuleItemDto> Items);
