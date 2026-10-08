namespace ProgressService.Contracts.Requests;

/// <summary>
///     Тело <c>POST /progress/quizzes/{quizId}/questions/{questionId}/check</c> (#556):
///     текущий ответ студента на ОДИН вопрос для немедленной проверки «на лету».
///     Для choice-вопросов заполняется <see cref="SelectedOptionIds"/>, для текстовых —
///     <see cref="TextAnswer"/>. Попытка не сохраняется — это самопроверка во время
///     прохождения COURSE-квиза.
/// </summary>
public sealed record CheckQuizQuestionRequest(
    IReadOnlyList<Guid>? SelectedOptionIds,
    string? TextAnswer);
