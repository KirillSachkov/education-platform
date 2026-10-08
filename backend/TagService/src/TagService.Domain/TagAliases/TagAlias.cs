using TagService.Domain.Tags;

namespace TagService.Domain.TagAliases;

/// <summary>
/// Алиас тега / Tag alias.
/// </summary>
public class TagAlias
{
    private TagAlias(TagAliasId id, TagId tagId, TagId aliasTagId)
    {
        Id = id;
        TagId = tagId;
        AliasTagId = aliasTagId;
    }

    // EF Core
    private TagAlias()
    {
    }

    /// <summary>
    /// Создает новый алиас тега / Creates a new tag alias.
    /// </summary>
    /// <param name="tagId">Идентификатор канонического тега / Canonical tag identifier.</param>
    /// <param name="aliasTagId">Идентификатор тега-алиаса / Alias tag identifier.</param>
    /// <returns>Результат создания алиаса / Alias creation result.</returns>
    public static Result<TagAlias, Error> Create(TagId tagId, TagId aliasTagId)
    {
        if (tagId is null)
            return GeneralErrors.ValueIsRequired("tagAlias.tagId");

        if (aliasTagId is null)
            return GeneralErrors.ValueIsRequired("tagAlias.aliasTagId");

        return new TagAlias(TagAliasId.Create(), tagId, aliasTagId);
    }

    /// <summary>
    /// Идентификатор алиаса тега / Tag alias identifier.
    /// </summary>
    public TagAliasId Id { get; private set; } = null!;

    /// <summary>
    /// Идентификатор канонического тега / Canonical tag identifier.
    /// </summary>
    public TagId TagId { get; private set; } = null!;

    /// <summary>
    /// Идентификатор тега-алиаса / Alias tag identifier.
    /// </summary>
    public TagId AliasTagId { get; private set; } = null!;
}
