namespace CommentService.Contracts.Comments.Dtos;

/// <summary>
/// Запись из инбокса комментариев пользователя — недавние ответы на его комментарии.
/// </summary>
public sealed record InboxCommentDto
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

    public Guid? ParentAuthorId { get; init; }

    /// <summary>
    /// Превью комментария-родителя (на который ответили) — первые ~280 символов.
    /// Если родитель удалён, поле = null.
    /// </summary>
    public string? ParentPreview { get; init; }

    /// <summary>
    /// Slug первого опубликованного курса, к которому привязан целевой материал
    /// (resolved через ECS <c>/internal/materials/course-bindings</c>). Null если
    /// материал — standalone (только в knowledge-base) либо если ECS недоступен
    /// в момент запроса (graceful fallback). Фронт использует это поле, чтобы
    /// открыть коммент на странице курса (где работает CommentSection и навигация
    /// по программе) вместо standalone KB-страницы без комментов.
    /// </summary>
    public string? TargetCourseSlug { get; init; }
}
