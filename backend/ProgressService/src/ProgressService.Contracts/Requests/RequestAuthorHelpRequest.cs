namespace ProgressService.Contracts.Requests;

/// <summary>
///     Опциональное тело <c>POST /progress/submissions/{id}/request-author-help/</c> (#575):
///     свободный текст «в чём нужна помощь». Может быть null/пустым — тогда автор получает
///     сигнал «позвали» без пояснения. Длина капается на бэке (<c>AUTHOR_HELP_MESSAGE_MAX_LENGTH</c>).
/// </summary>
public sealed record RequestAuthorHelpRequest(string? Message);
