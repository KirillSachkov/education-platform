namespace CommentService.Contracts.Comments.Dtos;

/// <summary>
/// Запись из ленты комментариев автора (`/comments/author-feed/`).
/// Включает данные коммента + контекст target-сущности + флаги для UI.
/// </summary>
public sealed record AuthorFeedCommentDto
{
    public Guid Id { get; init; }

    public Guid AuthorId { get; init; }

    public string? AuthorName { get; init; }

    public string? AuthorUsername { get; init; }

    public Guid? AuthorAvatarId { get; init; }

    public string? Content { get; init; }

    public int Depth { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }

    public bool IsDeleted { get; init; }

    public required string TargetEntityType { get; init; }

    public Guid TargetEntityId { get; init; }

    public Guid? ParentId { get; init; }

    public string? ParentPreview { get; init; }

    /// <summary>
    /// true, если автор контента уже отвечал в этом треде (любая глубина).
    /// </summary>
    public bool HasMyReply { get; init; }

    /// <summary>
    /// true, если коммент создан после последнего просмотра ленты автором.
    /// </summary>
    public bool IsUnread { get; init; }

    /// <summary>
    /// Title целевой сущности (например, заголовок материала). Резолвится cross-schema
    /// JOIN'ом в CommentService — может быть null, если материал удалён или enrichment failed.
    /// </summary>
    public string? TargetTitle { get; init; }
}
