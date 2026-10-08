namespace CommentService.Contracts.Comments.Requests;

/// <summary>
/// Запрос на получение комментариев / Request to get comments
/// </summary>
/// <param name="TargetType">Тип целевого объекта / Target object type</param>
/// <param name="TargetId">Идентификатор целевого объекта / Target object identifier</param>
/// <param name="Cursor">Курсор для пагинации / Cursor for pagination</param>
/// <param name="Limit">Максимальное количество комментариев / Maximum number of comments</param>
public record GetChildrenCommentsRequest(
    string TargetType,
    Guid TargetId,
    string? Cursor,
    int Limit = Constants.DEFAULT_PAGE_SIZE);
