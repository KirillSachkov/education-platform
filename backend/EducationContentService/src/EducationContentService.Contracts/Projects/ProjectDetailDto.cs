namespace EducationContentService.Contracts.Projects;

/// <summary>
///     Полная информация о проекте с перечнем задач.
/// </summary>
public sealed record ProjectDetailDto(
    Guid Id,
    Guid AuthorId,
    string Title,
    string? Description,
    string? DetailedDescription,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool RequiresGithubConnection,
    bool RequiresReviewApp,
    bool IsAutoReviewEnabled,
    IReadOnlyList<ProjectItemDto> Items);

/// <summary>
///     Задача проекта.
/// </summary>
public sealed record ProjectItemDto(
    Guid Id,
    Guid IssueId,
    string SortKey,
    bool IsOptional,
    int? MaxScore,
    string? Title,
    string? Status,
    string? AccessType,
    string SubmissionMode,
    string? SelfCheckInstructions);
