using Common;

namespace TagService.Domain.EntityTags;

/// <summary>
/// Ссылка на сущность, к которой привязан тег / Entity reference to which a tag is attached.
/// </summary>
public sealed record TagEntityReference : EntityReference
{
    private TagEntityReference(EntityType type, Guid id)
        : base(type, id)
    {
    }

    public static bool IsSupported(EntityType entityType) =>
        entityType is EntityType.Course
            or EntityType.Module
            or EntityType.Project
            or EntityType.Issue
            or EntityType.Material;

    public static Result<TagEntityReference, Error> Of(EntityType type, Guid id)
    {
        UnitResult<Error> validationResult = ValidateBase(type, id);
        if (validationResult.IsFailure)
        {
            return validationResult.Error;
        }

        if (!IsSupported(type))
        {
            return GeneralErrors.ValueIsInvalid("entityReference.type");
        }

        return new TagEntityReference(type, id);
    }

    public static Result<TagEntityReference, Error> Of(string type, Guid id)
    {
        if (!Enum.TryParse(type, true, out EntityType entityType))
        {
            return GeneralErrors.ValueIsInvalid("entityReference.type");
        }

        return Of(entityType, id);
    }
}
