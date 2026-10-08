namespace FileService.Domain;

public sealed record TargetEntity
{
    public const int MAX_TYPE_LENGTH = 100;

    private TargetEntity(string type, Guid id)
    {
        Type = type;
        Id = id;
    }

    public string Type { get; }

    public Guid Id { get; }

    public static Result<TargetEntity, Error> Of(string type, Guid id)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return GeneralErrors.ValueIsInvalid("targetEntity.type");
        }

        string normalizedType = type.Trim().ToLowerInvariant();
        if (normalizedType.Length > MAX_TYPE_LENGTH)
        {
            return GeneralErrors.ValueIsInvalid("targetEntity.type");
        }

        if (id == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid("targetEntity.id");
        }

        return new TargetEntity(normalizedType, id);
    }
}
