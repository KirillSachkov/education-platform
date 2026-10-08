namespace CommentService.Domain;

/// <summary>
/// Per-author cursor для author-feed'а: timestamp последнего просмотра ленты.
/// Используется для вычисления флага «непрочитанный» (created_at &gt; viewed_at).
/// </summary>
public sealed class AuthorFeedState
{
    private AuthorFeedState(Guid authorId, DateTime viewedAt)
    {
        AuthorId = authorId;
        ViewedAt = viewedAt;
    }

    // EF Core
    private AuthorFeedState()
    {
    }

    public Guid AuthorId { get; private set; }

    public DateTime ViewedAt { get; private set; }

    public static AuthorFeedState Create(Guid authorId, DateTime viewedAt) =>
        new(authorId, viewedAt);
}
