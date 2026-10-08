namespace CommentService.Contracts.Comments.Requests;

/// <summary>
/// Запрос инбокса комментариев пользователя.
/// </summary>
/// <param name="Limit">Максимальное число записей.</param>
public sealed record GetCommentInboxRequest(int Limit = Constants.DEFAULT_PAGE_SIZE);
