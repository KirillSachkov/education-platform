namespace TagService.Domain.EntityTags;

/// <summary>
/// Идентификатор связи сущности и тега / Entity-tag link identifier.
/// </summary>
public sealed record EntityTagId
{
    private EntityTagId(Guid value) => Value = value;

    /// <summary>
    /// Значение идентификатора / Identifier value.
    /// </summary>
    public Guid Value { get; private set; }

    /// <summary>
    /// Создает новый идентификатор связи сущности и тега / Creates a new entity-tag link identifier.
    /// </summary>
    public static EntityTagId Create() => new(Guid.CreateVersion7());

    /// <summary>
    /// Создает идентификатор связи сущности и тега из GUID / Creates an entity-tag link identifier from GUID.
    /// </summary>
    /// <param name="entityTagId">GUID связи сущности и тега / Entity-tag link GUID.</param>
    /// <returns>Идентификатор связи сущности и тега / Entity-tag link identifier.</returns>
    public static EntityTagId Of(Guid entityTagId) => new(entityTagId);

    /// <summary>
    /// Создает массив идентификаторов связей сущности и тега из массива GUID / Creates an array of entity-tag link identifiers from GUID array.
    /// </summary>
    /// <param name="entityTagIds">Массив GUID связей сущности и тега / Array of entity-tag link GUIDs.</param>
    /// <returns>Массив идентификаторов связей сущности и тега / Array of entity-tag link identifiers.</returns>
    public static EntityTagId[] Of(IEnumerable<Guid> entityTagIds) => [.. entityTagIds.Select(Of)];
}
