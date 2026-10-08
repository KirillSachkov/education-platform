using Common;

namespace EducationContentService.Core.Features.ProgressLookup;

public sealed record ResolvableMaterialEntityReference : EntityReference
{
    private ResolvableMaterialEntityReference(EntityType type, Guid id)
        : base(type, id)
    {
    }

    public static bool IsSupported(EntityType entityType) =>
        entityType is EntityType.Material or EntityType.Issue;

    public static Result<ResolvableMaterialEntityReference, Error> Of(EntityType type, Guid id)
    {
        UnitResult<Error> validationResult = ValidateBase(type, id);
        if (validationResult.IsFailure)
        {
            return validationResult.Error;
        }

        if (!IsSupported(type))
        {
            return GeneralErrors.ValueIsInvalid("target.type");
        }

        return new ResolvableMaterialEntityReference(type, id);
    }
}
