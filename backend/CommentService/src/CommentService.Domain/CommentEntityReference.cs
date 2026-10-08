using Common;

namespace CommentService.Domain;

public sealed record CommentEntityReference : EntityReference
{
    private CommentEntityReference(EntityType type, Guid id)
        : base(type, id)
    {
    }

    public static bool IsSupported(EntityType entityType) =>
        entityType is EntityType.Course
            or EntityType.Issue
            or EntityType.Quiz
            or EntityType.Material;

    public static Result<CommentEntityReference, Error> Of(EntityType type, Guid id)
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

        return new CommentEntityReference(type, id);
    }

    public static Result<CommentEntityReference, Error> Of(string type, Guid id)
    {
        if (string.IsNullOrWhiteSpace(type) ||
            !Enum.TryParse(type, ignoreCase: true, out EntityType entityType))
        {
            return GeneralErrors.ValueIsInvalid("entityReference.type");
        }

        return Of(entityType, id);
    }
}
