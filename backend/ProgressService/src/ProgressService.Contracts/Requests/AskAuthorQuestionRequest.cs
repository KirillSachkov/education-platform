namespace ProgressService.Contracts.Requests;

/// <summary>
///     Тело <c>POST /progress/issues/{issueId}/ask-author/</c> (#693): обязательный текст
///     вопроса студента автору по заданию (до отправки решения). Trim + длина капается на
///     бэке (<c>IssueAuthorQuestion.MESSAGE_MAX_LENGTH</c>).
/// </summary>
public sealed record AskAuthorQuestionRequest(string Message);
