namespace EducationContentService.Contracts.Projects;

/// <summary>
///     Сводка покрытия AI-review промптами по ВСЕМУ курсу за один вызов: rollup-сводка
///     + per-project блоки (<see cref="ProjectReviewCoverageDto"/>) со статусом
///     PROJECT-level guidelines и per-issue review-spec'ами. Покрывает задачи,
///     размещённые и в проектах (project_items), и в модулях (module_items) курса.
///     Read-only — для массового аудита/заполнения промптов через MCP (issue #356
///     дал project-level, это — course-level follow-up). Платформенный master-switch
///     AI-проверки (<c>reviewEnabled</c>) живёт в AssignmentReviewService и в эту
///     сводку не входит — читается отдельно через admin ai-settings.
/// </summary>
public sealed record CourseReviewCoverageDto(
    Guid CourseId,
    string CourseTitle,
    CourseReviewCoverageSummaryDto Summary,
    IReadOnlyList<ProjectReviewCoverageDto> Projects);

/// <summary>
///     Агрегированные счётчики по курсу — сразу видно масштаб «дыр» в покрытии,
///     не пробегая по каждому проекту. Все «With…» считают непустые значения
///     (длина &gt; 0), «AutoReviewDisabled» — где соответствующий тумблер выключен.
/// </summary>
public sealed record CourseReviewCoverageSummaryDto(
    int ProjectCount,
    int ProjectsWithContext,
    int ProjectsAutoReviewDisabled,
    int IssueCount,
    int IssuesWithReviewSpec,
    int IssuesWithAuthorPrompt,
    int IssuesWithReviewAspects,
    int IssuesAutoReviewDisabled);
