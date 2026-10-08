namespace ProgressService.Contracts.Responses;

/// <summary>
///     Состояние «задан ли вопрос автору» для текущего пользователя по заданию (#693).
///     <c>AskedAt</c> = время первого вопроса (UTC) или <c>null</c>, если вопрос не задавался.
///     Питает фронт-состояние «Вопрос отправлен автору».
/// </summary>
public sealed record AuthorQuestionStateResponse(DateTime? AskedAt);
