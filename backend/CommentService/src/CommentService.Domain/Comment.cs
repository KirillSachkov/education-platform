namespace CommentService.Domain;

/// <summary>
/// Представляет комментарий в системе / Represents a comment in the system.
/// </summary>
public sealed class Comment
{
    private Comment(
        CommentId id,
        CommentEntityReference entityReference,
        Guid authorId,
        Content content,
        Path path,
        Guid? targetAuthorId)
    {
        Id = id;
        EntityReference = entityReference;
        AuthorId = authorId;
        Content = content;
        Path = path;
        TargetAuthorId = targetAuthorId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
        IsDeleted = false;
        DeletedAt = null;
    }

    // EF Core
    private Comment()
    {
    }

    /// <summary>
    /// Создает родительский комментарий / Creates a parent comment.
    /// </summary>
    /// <param name="authorId">Идентификатор автора / Author identifier.</param>
    /// <param name="entityReference">Ссылка на сущность / Entity reference.</param>
    /// <param name="content">Содержимое комментария / Comment content.</param>
    /// <param name="id">Идентификатор комментария (опционально) / Comment identifier (optional).</param>
    /// <param name="targetAuthorId">Текущий владелец целевой сущности / Current target owner.</param>
    /// <returns>Новый родительский комментарий / New parent comment.</returns>
    public static Comment CreateParent(
        Guid authorId,
        CommentEntityReference entityReference,
        Content content,
        CommentId? id = null,
        Guid? targetAuthorId = null)
    {
        CommentId actualId = id ?? CommentId.Create();

        Path path = Path.CreateParent(actualId.Value);

        return new Comment(actualId, entityReference, authorId, content, path, targetAuthorId);
    }

    /// <summary>
    /// Создает дочерний комментарий / Creates a child comment.
    /// </summary>
    /// <param name="parent">Родительский комментарий / Parent comment.</param>
    /// <param name="authorId">Идентификатор автора / Author identifier.</param>
    /// <param name="content">Содержимое комментария / Comment content.</param>
    /// <param name="id">Идентификатор комментария (опционально) / Comment identifier (optional).</param>
    /// <param name="targetAuthorId">Текущий владелец целевой сущности / Current target owner.</param>
    /// <returns>Новый дочерний комментарий / New child comment.</returns>
    public static Comment CreateChild(
        Comment parent,
        Guid authorId,
        Content content,
        CommentId? id = null,
        Guid? targetAuthorId = null)
    {
        CommentId actualId = id ?? CommentId.Create();

        Path path = parent.Path.CreateChild(actualId.Value);
        CommentEntityReference entityReference = CommentEntityReference.Of(
            parent.EntityReference.Type,
            parent.EntityReference.Id).Value;

        return new Comment(actualId, entityReference, authorId, content, path, targetAuthorId);
    }

    /// <summary>
    /// Уникальный идентификатор комментария / Unique comment identifier.
    /// </summary>
    public CommentId Id { get; } = null!;

    /// <summary>
    /// Уникальный идентификатор автора комментария / Unique author identifier.
    /// </summary>
    public Guid AuthorId { get; }

    /// <summary>
    /// Ссылка на сущность, к которой относится комментарий / Entity reference to which the comment belongs.
    /// </summary>
    public CommentEntityReference EntityReference { get; private set; } = null!;

    /// <summary>
    /// Содержимое комментария в формате Markdown / Comment content in Markdown format.
    /// </summary>
    public Content Content { get; private set; } = null!;

    /// <summary>
    /// Путь комментария в иерархии / Comment path in the hierarchy.
    /// </summary>
    public Path Path { get; private set; } = null!;

    /// <summary>
    /// Денормализованный идентификатор автора целевой сущности (материала/курса/issue),
    /// под которой оставлен комментарий. Используется author-feed'ом для быстрой фильтрации.
    /// Может быть null для исторических записей и для случаев, когда ownership lookup
    /// не сработал на момент создания.
    /// </summary>
    public Guid? TargetAuthorId { get; private set; }

    /// <summary>
    /// Дата и время создания комментария / Comment creation date and time.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Дата и время последнего обновления комментария / Comment last update date and time.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Флаг, указывающий, удален ли комментарий / Flag indicating whether the comment is deleted.
    /// </summary>
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// Дата и время удаления комментария / Comment deletion date and time.
    /// </summary>
    public DateTime? DeletedAt { get; private set; }

    /// <summary>
    /// Обновляет содержимое комментария / Updates the comment content.
    /// </summary>
    /// <param name="content">Новое содержимое / New content.</param>
    public void Update(Content content)
    {
        Content = content;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Выполняет мягкое удаление комментария / Performs soft delete of the comment.
    /// </summary>
    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Восстанавливает удаленный комментарий / Restores the deleted comment.
    /// </summary>
    public void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
