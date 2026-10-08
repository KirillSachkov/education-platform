namespace ProgressService.Contracts.Responses;

/// <summary>
///     Админ-аналитика по всем тестам платформы (#556, AC5). Плоский список квизов
///     (одна строка на квиз с ≥1 попыткой) + top-line KPI; группировку по курсам
///     фронт делает client-side по <see cref="QuizAdminOverviewRow.CourseId"/> /
///     <see cref="QuizAdminOverviewRow.CourseTitle"/>. LEVEL_TEST-квизы исключены —
///     у воронки своя админ-страница (#537). Зеркало level-test admin-overview по стилю.
/// </summary>
public sealed record QuizAdminOverviewResponse(
    int TotalQuizzes,
    long TotalAttempts,
    double OverallPassRatePercent,
    double OverallAvgScorePercent,
    IReadOnlyList<QuizAdminOverviewRow> Quizzes);

/// <summary>
///     Агрегат по одному квизу. <see cref="CourseId"/>/<see cref="CourseTitle"/> —
///     представительный курс (MIN(course_id) из course_quizzes), <c>null</c> для
///     standalone-теста (фронт группирует такие в «Без курса»).
///     <see cref="PassRatePercent"/> — доля попыток с зачётом, <see cref="AvgScorePercent"/> —
///     средний балл по всем попыткам.
/// </summary>
public sealed record QuizAdminOverviewRow(
    Guid QuizId,
    string Title,
    Guid? CourseId,
    string? CourseTitle,
    long AttemptsCount,
    long UniqueUsers,
    double PassRatePercent,
    double AvgScorePercent);

/// <summary>
///     Drill-in по конкретному тесту (#556, AC5): распределение баллов по 5 бакетам и
///     сложность каждого вопроса (доля верных ответов по всем попыткам), чтобы автор
///     видел «самые сложные вопросы». Per-вопрос корректность пересчитывается из
///     сохранённых ответов попыток тем же грейдером, что и при сабмите.
/// </summary>
public sealed record QuizAdminStatsResponse(
    Guid QuizId,
    string Title,
    long AttemptsCount,
    long UniqueUsers,
    double PassRatePercent,
    double AvgScorePercent,
    IReadOnlyList<QuizScoreBucketRow> ScoreDistribution,
    IReadOnlyList<QuizQuestionStatsRow> Questions);

/// <summary>Бакет распределения баллов (<c>«0-20»…«81-100»</c>) и число попавших в него попыток.</summary>
public sealed record QuizScoreBucketRow(string Bucket, long Count);

/// <summary>
///     Статистика по одному вопросу теста. <see cref="AnsweredCount"/> — сколько попыток
///     дали ответ на этот вопрос; <see cref="CorrectCount"/> — сколько из них верны
///     (только автогрейдируемые типы; OPEN_TEXT в подсчёт верных не входит).
///     <see cref="CorrectRatePercent"/> — доля верных среди ответивших.
/// </summary>
public sealed record QuizQuestionStatsRow(
    Guid QuestionId,
    string Text,
    string Type,
    long AnsweredCount,
    long CorrectCount,
    double CorrectRatePercent);
