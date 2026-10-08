using TagService.Domain.Tags;

namespace TagService.Domain.EntityTags;

/// <summary>
/// Связь сущности и тега / Entity-tag link.
/// </summary>
public class EntityTag
{
    private EntityTag(EntityTagId id, TagEntityReference entityReference, TagId tagId)
    {
        Id = id;
        EntityReference = entityReference;
        TagId = tagId;
    }

    // EF Core
    private EntityTag()
    {
    }

    /// <summary>
    /// Создает новую связь сущности и тега / Creates a new entity-tag link.
    /// </summary>
    /// <param name="entityReference">Ссылка на сущность / Entity reference.</param>
    /// <param name="tagId">Идентификатор тега / Tag identifier.</param>
    /// <returns>Результат создания связи / Link creation result.</returns>
    public static Result<EntityTag, Error> Create(TagEntityReference entityReference, TagId tagId)
    {
        if (entityReference is null)
            return GeneralErrors.ValueIsRequired("entityTag.entityReference");

        if (tagId is null)
            return GeneralErrors.ValueIsRequired("entityTag.tagId");

        return new EntityTag(EntityTagId.Create(), entityReference, tagId);
    }

    /// <summary>
    /// Идентификатор связи сущности и тега / Entity-tag link identifier.
    /// </summary>
    public EntityTagId Id { get; private set; } = null!;

    /// <summary>
    /// Ссылка на сущность / Entity reference.
    /// </summary>
    public TagEntityReference EntityReference { get; private set; } = null!;

    /// <summary>
    /// Идентификатор тега / Tag identifier.
    /// </summary>
    public TagId TagId { get; private set; } = null!;
}
