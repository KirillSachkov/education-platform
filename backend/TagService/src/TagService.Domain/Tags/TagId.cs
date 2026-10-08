namespace TagService.Domain.Tags;

/// <summary>
/// Идентификатор тега / Tag identifier.
/// </summary>
public sealed record TagId
{
    private TagId(Guid value) => Value = value;

    /// <summary>
    /// Значение идентификатора / Identifier value.
    /// </summary>
    public Guid Value { get; private set; }

    /// <summary>
    /// Создает новый идентификатор тега / Creates a new tag identifier.
    /// </summary>
    public static TagId Create() => new(Guid.CreateVersion7());

    /// <summary>
    /// Создает идентификатор тега из GUID / Creates a tag identifier from GUID.
    /// </summary>
    /// <param name="tagId">GUID тега / Tag GUID.</param>
    /// <returns>Идентификатор тега / Tag identifier.</returns>
    public static TagId Of(Guid tagId) => new(tagId);

    /// <summary>
    /// Создает массив идентификаторов тегов из массива GUID / Creates an array of tag identifiers from GUID array.
    /// </summary>
    /// <param name="tagIds">Массив GUID тегов / Array of tag GUIDs.</param>
    /// <returns>Массив идентификаторов тегов / Array of tag identifiers.</returns>
    public static TagId[] Of(IEnumerable<Guid> tagIds) => [.. tagIds.Select(Of)];
}
