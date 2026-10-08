namespace ProgressService.Contracts.Requests;

/// <summary>
///     Тело <c>POST /progress/level-test/attempts</c>: ответы респондента на вопросы
///     level-test'а. Аутентифицированный вызов — UserId из токена, <see cref="AnonymousId"/>
///     игнорируется; анонимный — AnonymousId (UUID из cookie <c>plu_anon_id</c>) обязателен.
///     Вопросы без ответа можно не присылать (choice → неверно, open_text → не грейдится).
///     Issue #479.
/// </summary>
public sealed record SubmitLevelTestAttemptRequest(
    Guid QuizId,
    string? AnonymousId,
    IReadOnlyList<SubmitLevelTestAnswerItem> Answers);

public sealed record SubmitLevelTestAnswerItem(
    Guid QuestionId,
    IReadOnlyList<Guid>? SelectedOptionIds,
    string? TextAnswer);

/// <summary>
///     Тело <c>POST /progress/level-test/attempts/claim</c>: привязывает ВСЕ
///     неклеймленные попытки этого анонимного идентификатора к текущему пользователю.
/// </summary>
public sealed record ClaimLevelTestAttemptsRequest(string AnonymousId);
