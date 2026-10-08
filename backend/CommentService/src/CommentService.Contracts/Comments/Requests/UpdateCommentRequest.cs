namespace CommentService.Contracts.Comments.Requests;

/// <summary>
/// Запрос на обновление комментария / Update comment request.
/// </summary>
/// <param name="Content">Содержимое комментария / Comment content.</param>
public record UpdateCommentRequest(string Content);