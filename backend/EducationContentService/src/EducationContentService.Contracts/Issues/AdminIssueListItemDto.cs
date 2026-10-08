namespace EducationContentService.Contracts.Issues;

/// <summary>
///     Flat admin row for an issue with its project + course + module placement.
///     Used by the admin issue list/search/export endpoint (MCP course-task tooling).
///     <see cref="Content"/> is only populated when the caller requests it
///     (<c>includeContent=true</c>) to keep list responses small.
/// </summary>
public sealed record AdminIssueListItemDto(
    Guid IssueId,
    Guid ProjectId,
    string ProjectTitle,
    string Title,
    string Status,
    string AccessType,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? ProjectSortKey,
    Guid? CourseId,
    string? CourseTitle,
    string? CourseProjectSortKey,
    Guid? ModuleId,
    string? ModuleTitle,
    string? ModuleSortKey,
    int InternalMaterialsCount,
    string? Content);
