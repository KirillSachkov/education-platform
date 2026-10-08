namespace ProgressService.Contracts.Responses;

/// <summary>
///     Author per-course аналитика по тестам (#634): тесты ЭТОГО курса с агрегатами
///     попыток. Зеркало <see cref="QuizAdminOverviewResponse"/>, но скоупится одним
///     курсом (квизы из его <c>module_items</c>), поэтому без полей курса в строке.
///     LEVEL_TEST исключён (в blueprint.QuizIds его нет; defense-in-depth — фильтр по
///     Purpose на enrichment'е). Доступ — владелец курса / admin / content-moderator.
/// </summary>
public sealed record CourseQuizStatsOverviewResponse(
    Guid CourseId,
    int TotalQuizzesWithAttempts,
    long TotalAttempts,
    double OverallPassRatePercent,
    double OverallAvgScorePercent,
    IReadOnlyList<CourseQuizStatsRow> Quizzes);

/// <summary>
///     Агрегат по одному тесту курса: число попыток, уникальные студенты, доля зачётов,
///     средний балл по всем попыткам. Drill-in (распределение баллов + correct-rate по
///     вопросам) отдаётся отдельным эндпоинтом в форме <see cref="QuizAdminStatsResponse"/>.
/// </summary>
public sealed record CourseQuizStatsRow(
    Guid QuizId,
    string Title,
    long AttemptsCount,
    long UniqueUsers,
    double PassRatePercent,
    double AvgScorePercent);

/// <summary>
///     Author per-course аналитика прогресса (#634): вовлечённость и активность студентов
///     курса. <see cref="EngagedStudents"/>/<see cref="CompletedStudents"/>/<see cref="AverageProgressPercent"/>
///     считаются той же CTE, что и публичный <see cref="CoursePublicStatsResponse"/>
///     (engaged = реальная активность, completed ≥80% материалов), плюс три author-only
///     счётчика: отметок «изучено» по материалам курса, сдач заданий и студентов,
///     сдавших хотя бы один тест курса.
/// </summary>
public sealed record CourseProgressStatsResponse(
    long EngagedStudents,
    long CompletedStudents,
    double AverageProgressPercent,
    long MaterialViewsCompleted,
    long IssueSubmissionsCount,
    long QuizPassersCount);
