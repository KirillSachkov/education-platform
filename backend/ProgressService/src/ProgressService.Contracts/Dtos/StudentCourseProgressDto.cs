namespace ProgressService.Contracts.Dtos;

/// <summary>
///     Прогресс-факты КОНКРЕТНОГО студента по курсу для staff-просмотра (автор/админ/модератор).
///     Возвращает только данные ProgressService (изученные материалы + статусы заданий) — структуру
///     курса фронт уже знает (course-builder DTO) и накладывает статусы сверху.
///     access-derive-model (#367): enrollment ленивый. Если у студента нет ни одной прогресс-строки
///     (grant-holder, ещё не начинал) — <see cref="EnrollmentStarted"/> = false, массивы пустые.
/// </summary>
public sealed record StudentCourseProgressDto(
    Guid CourseId,
    Guid UserId,
    bool EnrollmentStarted,
    DateTime? EnrolledAt,
    IReadOnlyList<StudentCompletedMaterialDto> CompletedMaterials,
    IReadOnlyList<StudentIssueProgressDto> Issues);

/// <summary>
///     Материал, который студент явно отметил «Изучено» (<c>material_views.is_completed = TRUE</c>).
///     Silent track-view'ы (mount detail-страницы, issue #285) сюда НЕ попадают.
/// </summary>
public sealed record StudentCompletedMaterialDto(
    Guid MaterialId,
    DateTime CompletedAt);

/// <summary>
///     Статус задания студента в курсе. <paramref name="Status"/> — статус <c>issue_progress</c>
///     (NOT_STARTED / IN_PROGRESS / UNDER_REVIEW / COMPLETED / REQUESTED_CHANGES);
///     <paramref name="ReviewStatus"/> — review-статус последней попытки
///     (PENDING / IN_REVIEW / APPROVED / CHANGES_REQUESTED), null если попыток ещё не было.
/// </summary>
public sealed record StudentIssueProgressDto(
    Guid IssueId,
    Guid ProjectId,
    string Status,
    string? ReviewStatus,
    DateTime? SubmittedAt,
    int AttemptsCount);
