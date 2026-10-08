namespace CommentService.Contracts.Comments.Requests;

/// <summary>
/// Запрос на получение плоского треда комментариев / Request to get a flat comments thread.
/// </summary>
/// <param name="TargetType">Тип целевого объекта / Target object type</param>
/// <param name="TargetId">Идентификатор целевого объекта / Target object identifier</param>
/// <param name="Cursor">Курсор для пагинации / Cursor for pagination</param>
/// <param name="Limit">Максимальное количество комментариев / Maximum number of comments</param>
public record GetThreadCommentsRequest(
    string TargetType,
    Guid TargetId,
    string? Cursor,
    int Limit = Constants.DEFAULT_PAGE_SIZE);
