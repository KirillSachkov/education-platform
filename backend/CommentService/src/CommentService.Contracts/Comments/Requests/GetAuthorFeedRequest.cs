namespace CommentService.Contracts.Comments.Requests;

/// <summary>
/// Запрос ленты комментариев автора (`GET /comments/author-feed/`).
/// </summary>
/// <param name="Cursor">Base64Url-курсор предыдущей страницы (опционально).</param>
/// <param name="Limit">Размер страницы (1..100, default 20).</param>
/// <param name="WithoutReply">Только комментарии без ответа автора в треде.</param>
/// <param name="UnreadOnly">Только записи, созданные после последнего просмотра ленты.</param>
public sealed record GetAuthorFeedRequest(
    string? Cursor = null,
    int Limit = Constants.DEFAULT_PAGE_SIZE,
    bool WithoutReply = false,
    bool UnreadOnly = false);
