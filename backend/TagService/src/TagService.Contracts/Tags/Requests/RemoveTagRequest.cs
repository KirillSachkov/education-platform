namespace TagService.Contracts.Tags.Requests;

/// <summary>
/// Запрос на удаление тегов у сущности / Request to remove tags from an entity.
/// </summary>
public sealed record RemoveTagRequest
{
    /// <summary>
    /// Тип сущности / Entity type.
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Идентификатор сущности / Entity identifier.
    /// </summary>
    public required Guid EntityId { get; init; }

    /// <summary>
    /// Идентификаторы тегов для удаления / Tag identifiers to remove.
    /// </summary>
    public IReadOnlyList<Guid> TagIds { get; init; } = [];
}
