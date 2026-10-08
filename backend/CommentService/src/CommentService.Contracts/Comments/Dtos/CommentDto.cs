namespace CommentService.Contracts.Comments.Dtos;

/// <summary>
/// DTO комментария / Comment DTO.
/// </summary>
public record CommentDto
{
    /// <summary>
    /// Идентификатор комментария / Comment identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Идентификатор автора комментария / Comment author identifier.
    /// </summary>
    public Guid AuthorId { get; init; }

    /// <summary>
    /// Имя автора комментария / Comment author name.
    /// </summary>
    public string? AuthorName { get; init; }

    /// <summary>
    /// Username автора комментария / Comment author username.
    /// </summary>
    public string? AuthorUsername { get; init; }

    /// <summary>
    /// Идентификатор аватара автора комментария / Comment author avatar identifier.
    /// </summary>
    public Guid? AuthorAvatarId { get; init; }

    /// <summary>
    /// Содержимое комментария / Comment content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Глубина вложенности комментария / Comment nesting depth.
    /// </summary>
    public required int Depth { get; init; }

    /// <summary>
    /// Идентификатор родительского комментария / Parent comment identifier.
    /// </summary>
    public Guid? ParentId { get; init; }

    /// <summary>
    /// Превью родительского комментария / Parent comment preview.
    /// </summary>
    public string? ParentPreview { get; init; }

    /// <summary>
    /// Дата и время создания комментария / Comment creation date and time.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Дата и время обновления комментария / Comment update date and time.
    /// </summary>
    public required DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Удален ли комментарий / Is the comment deleted.
    /// </summary>
    public bool IsDeleted { get; init; }

    /// <summary>
    /// Есть ли еще дочерние комментарии / Are there more children comments.
    /// </summary>
    public bool HasMoreChildren { get; init; }

    /// <summary>
    /// Общее количество прямых дочерних комментариев (replies depth=parent.depth+1, без soft-deleted).
    /// Для root-эндпоинта используется как счётчик «N ответов» в UI.
    /// </summary>
    public int ChildrenCount { get; init; }

    /// <summary>
    /// Превью первых N прямых ответов (depth=parent.depth+1).
    /// Заполняется ТОЛЬКО для root-листинга (<c>GET /comments</c>).
    /// Для children/thread-эндпоинтов — null.
    /// Позволяет фронту авто-раскрыть первый уровень без N+1 запросов.
    /// </summary>
    public IReadOnlyList<CommentDto>? PreviewChildren { get; init; }

    /// <summary>
    /// Cursor для дозагрузки следующих ответов через <c>GET /comments/{id}</c>.
    /// null когда <see cref="PreviewChildren"/> вместил все ответы (т.е.
    /// <see cref="ChildrenCount"/> &lt;= preview.Count). Передаётся фронтом
    /// как <c>cursor</c>-параметр в дозагрузке children.
    /// </summary>
    public string? PreviewChildrenNextCursor { get; init; }
}
