namespace ProgressService.Contracts.Requests;

/// <summary>
///     Тело <c>POST /progress/quizzes/{quizId}/attempts</c>: ответы студента на вопросы
///     квиза. Для choice-вопросов заполняется <see cref="SubmitQuizAnswerItem.SelectedOptionIds"/>,
///     для открытых — <see cref="SubmitQuizAnswerItem.TextAnswer"/>. Вопросы без ответа
///     можно не присылать — они считаются неотвеченными (choice → неверно). Issue #470.
/// </summary>
public sealed record SubmitQuizAttemptRequest(IReadOnlyList<SubmitQuizAnswerItem> Answers);

public sealed record SubmitQuizAnswerItem(
    Guid QuestionId,
    IReadOnlyList<Guid>? SelectedOptionIds,
    string? TextAnswer);
