namespace CommentService.Domain;

/// <summary>
/// Идентификатор комментария / Comment identifier.
/// </summary>
public sealed record CommentId
{
    private CommentId(Guid value) => Value = value;

    /// <summary>
    /// Значение идентификатора / Identifier value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Создает новый идентификатор комментария / Creates a new comment identifier.
    /// </summary>
    public static CommentId Create() => new(Guid.CreateVersion7());

    /// <summary>
    /// Создает идентификатор комментария из GUID / Creates a comment identifier from GUID.
    /// </summary>
    /// <param name="commentId">GUID комментария / Comment GUID.</param>
    public static CommentId Of(Guid commentId) => new(commentId);

    /// <summary>
    /// Создает массив идентификаторов комментариев из массива GUID / Creates an array of comment identifiers from GUID array.
    /// </summary>
    /// <param name="commentIds">Массив GUID комментариев / Array of comment GUIDs.</param>
    public static CommentId[] Of(Guid[] commentIds) => commentIds.Select(Of).ToArray();
}