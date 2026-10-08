namespace ProgressService.Contracts.Responses;

/// <summary>
///     Результат попытки прохождения квиза — full-reveal после сабмита (квиз — учебный
///     инструмент, не анти-чит): по каждому вопросу раскрываются правильные варианты,
///     а для открытых вопросов — эталонный ответ для самопроверки. Issue #470.
/// </summary>
public sealed record QuizAttemptResultResponse(
    Guid AttemptId,
    int ScorePercent,
    bool Passed,
    int PassingScorePercent,
    DateTime SubmittedAt,
    IReadOnlyList<QuizAttemptQuestionResultResponse> Questions);

/// <summary>
///     Per-вопрос разбор попытки. <see cref="Correct"/>: true/false для choice-вопросов,
///     <c>null</c> для OPEN_TEXT (не автогрейдится, в score не входит — самопроверка
///     по <see cref="ReferenceAnswer"/>).
///     <see cref="Options"/> (id+text) — снапшот вариантов из answer-key на момент чтения:
///     разбор рисуется из result-DTO, а не из «живого» квиза, поэтому правка теста автором
///     не ломает раскраску (#556). Пустой для текстовых вопросов.
/// </summary>
public sealed record QuizAttemptQuestionResultResponse(
    Guid QuestionId,
    string Type,
    bool? Correct,
    IReadOnlyList<Guid> SelectedOptionIds,
    IReadOnlyList<Guid> CorrectOptionIds,
    string? TextAnswer,
    string? ReferenceAnswer,
    IReadOnlyList<QuizAttemptOptionResultResponse> Options,
    string? Explanation = null);

/// <summary>Вариант ответа в разборе попытки (id+text) — для self-contained ревью-экрана (#556).</summary>
public sealed record QuizAttemptOptionResultResponse(Guid Id, string Text);

/// <summary>
///     Попытки текущего пользователя по квизу: best — максимальный балл (при равенстве —
///     более поздняя), last — последняя по времени. Обе с per-вопрос разбором для
///     ревью-экрана. Нет попыток → оба поля <c>null</c>.
/// </summary>
public sealed record MyQuizAttemptsResponse(
    QuizAttemptResultResponse? Best,
    QuizAttemptResultResponse? Last);

/// <summary>
///     Результат проверки ОДНОГО вопроса «на лету» (#556) — немедленная обратная связь
///     в COURSE-квизе без сохранения попытки. Ключ ответов не доезжает до студента
///     заранее: вопрос грейдится на сервере по уже зафиксированному ответу.
///     <see cref="Correct"/>: true/false для choice/EXACT_TEXT, <c>null</c> для OPEN_TEXT
///     (не автогрейдится — самопроверка по <see cref="ReferenceAnswer"/>).
///     <see cref="Options"/> (id+text) — снапшот вариантов для раскраски на клиенте.
/// </summary>
public sealed record CheckQuizQuestionResponse(
    Guid QuestionId,
    string Type,
    bool? Correct,
    IReadOnlyList<Guid> CorrectOptionIds,
    string? ReferenceAnswer,
    IReadOnlyList<QuizAttemptOptionResultResponse> Options,
    string? Explanation = null);

/// <summary>
///     Сводка тестов, которые проходил текущий пользователь (страница «Мои тесты», #556).
///     Одна строка на квиз — агрегат по всем попыткам: лучший/последний балл, число
///     попыток, итоговый pass (любая попытка прошла). LEVEL_TEST-квизы исключены (у
///     воронки своя страница). Сортировка — по последней активности (новые сверху).
/// </summary>
public sealed record MyQuizAttemptsSummaryResponse(
    IReadOnlyList<MyQuizAttemptsSummaryItem> Items,
    int TotalQuizzesTaken,
    int PassedCount,
    int AvgBestScorePercent);

/// <summary>
///     Один пройденный тест в сводке. <see cref="CourseId"/> — представительный курс
///     теста (null для standalone). <see cref="BestScorePercent"/> — максимум по
///     попыткам, <see cref="LastScorePercent"/> — балл последней попытки.
///     <see cref="Passed"/> — прошёл ли тест хотя бы в одной попытке.
/// </summary>
public sealed record MyQuizAttemptsSummaryItem(
    Guid QuizId,
    string Title,
    Guid? CourseId,
    int AttemptsCount,
    int BestScorePercent,
    int LastScorePercent,
    DateTime LastSubmittedAt,
    bool Passed);
