namespace EducationContentService.Contracts.Projects;

/// <summary>
///     Сводка покрытия AI-review промптами по проекту: статус PROJECT-level
///     guidelines + статус per-issue review-spec у каждой задачи. Read-only,
///     для массового аудита/заполнения промптов (через MCP) — одним вызовом
///     видно, у каких задач spec ещё пуст. Issue #356 (follow-up #15/#334).
/// </summary>
public sealed record ProjectReviewCoverageDto(
    Guid ProjectId,
    string ProjectTitle,
    bool HasProjectContext,
    bool ProjectIsAutoReviewEnabled,
    int GuidelinesLength,
    DateTime? ProjectContextUpdatedAt,
    IReadOnlyList<IssueReviewCoverageDto> Issues);

/// <summary>
///     Статус AI-review spec одной задачи проекта. <see cref="HasReviewSpec"/> = false
///     означает, что spec ещё не создан (длины = 0, <see cref="IsAutoReviewEnabled"/>
///     отражает эффективный дефолт true).
/// </summary>
public sealed record IssueReviewCoverageDto(
    Guid IssueId,
    string? Title,
    string? Status,
    bool HasReviewSpec,
    int AuthorPromptLength,
    int ReviewAspectsLength,
    bool IsAutoReviewEnabled,
    DateTime? ReviewSpecUpdatedAt);
