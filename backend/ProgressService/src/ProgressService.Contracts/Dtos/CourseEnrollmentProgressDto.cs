namespace ProgressService.Contracts.Dtos;

/// <summary>
///     Состояние и прогресс ученика по курсу для frontend.
/// </summary>
/// <param name="Source">
///     Откуда взялось зачисление: SELF (запись через UI), ADMIN (админ записал),
///     GITHUB_ORG (auto-enrollment v2 по членству в GitHub-org), и т.д. Значение
///     C# enum <c>EnrollmentSource</c> в UPPER_SNAKE_CASE. Под access-derive-model
///     (#367) новые anchor'ы — ENGAGEMENT / AUTHOR_SELF; access определяется грантами.
/// </param>
public sealed record CourseEnrollmentProgressDto(
    Guid EnrollmentId,
    Guid CourseId,
    string Source,
    int MaterialsTotal,
    int MaterialsViewed,
    int ModulesTotal,
    int ModulesCompleted,
    int IssuesTotal,
    int IssuesCompleted,
    DateTime EnrolledAt);
