using CSharpFunctionalExtensions;
using SharedKernel;

namespace Common;

public abstract record EntityReference
{
    public const int MAX_TYPE_LENGTH = 16;

    protected EntityReference(EntityType type, Guid id)
    {
        Type = type;
        Id = id;
    }

    public EntityType Type { get; }

    public Guid Id { get; }

    protected static UnitResult<Error> ValidateBase(
        EntityType type,
        Guid id)
    {
        if (!Enum.IsDefined(type))
        {
            return GeneralErrors.ValueIsInvalid("entityReference.type");
        }

        if (id == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid("entityReference.id");
        }

        return UnitResult.Success<Error>();
    }
}
