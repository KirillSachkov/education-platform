namespace TagService.Contracts.Tags.Requests;

/// <summary>
/// Запрос на добавление набора тегов к сущности / Request to add entity tags set.
/// </summary>
public sealed record AddTagsRequest
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
    /// Идентификаторы тегов для привязки к сущности / Tag identifiers to link to an entity.
    /// </summary>
    public IReadOnlyList<Guid> TagIds { get; init; } = [];

    /// <summary>
    /// Заголовки тегов для привязки к сущности / Tag titles to link to an entity.
    /// </summary>
    public IReadOnlyList<string> TagTitles { get; init; } = [];
}