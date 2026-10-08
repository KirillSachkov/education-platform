namespace CommentService.Contracts.Comments.Dtos;

/// <summary>
/// Цепочка предков комментария — от root к прямому родителю / Comment ancestor chain — from root to direct parent.
/// Используется фронтом на deep-link <c>?focus=&lt;id&gt;</c>, чтобы форс-раскрыть всех ancestors
/// до того, как браузер прыгнет на целевой коммент.
/// </summary>
public sealed record CommentAncestorsDto
{
    /// <summary>
    /// Тип сущности, к которой относится комментарий / Target entity type.
    /// </summary>
    public required string TargetType { get; init; }

    /// <summary>
    /// Идентификатор сущности, к которой относится комментарий / Target entity identifier.
    /// </summary>
    public required Guid TargetId { get; init; }

    /// <summary>
    /// Глубина вложенности комментария / Comment depth (0 = root).
    /// </summary>
    public required int Depth { get; init; }

    /// <summary>
    /// Цепочка предков, упорядоченная root → … → direct parent. Сам коммент в список НЕ входит.
    /// Пустая для root-комментариев (depth=0).
    /// </summary>
    public required IReadOnlyList<Guid> AncestorIds { get; init; }
}
