namespace TagService.Domain.TagAliases;

/// <summary>
/// Идентификатор алиаса тега / Tag alias identifier.
/// </summary>
public sealed record TagAliasId
{
    private TagAliasId(Guid value) => Value = value;

    /// <summary>
    /// Значение идентификатора / Identifier value.
    /// </summary>
    public Guid Value { get; private set; }

    /// <summary>
    /// Создает новый идентификатор алиаса тега / Creates a new tag alias identifier.
    /// </summary>
    public static TagAliasId Create() => new(Guid.CreateVersion7());

    /// <summary>
    /// Создает идентификатор алиаса тега из GUID / Creates a tag alias identifier from GUID.
    /// </summary>
    /// <param name="tagAliasId">GUID алиаса тега / Tag alias GUID.</param>
    /// <returns>Идентификатор алиаса тега / Tag alias identifier.</returns>
    public static TagAliasId Of(Guid tagAliasId) => new(tagAliasId);

    /// <summary>
    /// Создает массив идентификаторов алиасов тегов из массива GUID / Creates an array of tag alias identifiers from GUID array.
    /// </summary>
    /// <param name="tagAliasIds">Массив GUID алиасов тегов / Array of tag alias GUIDs.</param>
    /// <returns>Массив идентификаторов алиасов тегов / Array of tag alias identifiers.</returns>
    public static TagAliasId[] Of(IEnumerable<Guid> tagAliasIds) => [.. tagAliasIds.Select(Of)];
}
