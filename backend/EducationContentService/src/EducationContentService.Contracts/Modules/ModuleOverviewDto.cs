namespace EducationContentService.Contracts.Modules;

/// <summary>
///     Обзор модуля для студента — без admin-полей, только published данные.
/// </summary>
public sealed record ModuleOverviewDto(
    Guid Id,
    string Title,
    string? Description,
    string? DetailedDescription,
    int LessonCount,
    int IssueCount,
    IReadOnlyList<ModuleOverviewItemDto> Items);

/// <summary>
///     Элемент модуля в обзоре (урок или задача).
/// </summary>
public sealed record ModuleOverviewItemDto(
    Guid Id,
    string ItemType,
    string Title,
    bool IsOptional,
    string ViewPriority);
