namespace EducationContentService.Domain.ValueObjects;

/// <summary>
///     Value Object — идентификатор изображения (обложки).
///     Инвариант: не может быть <see cref="Guid.Empty"/>.
/// </summary>
public sealed record ImageId
{
    private ImageId(Guid value) => Value = value;

    public Guid Value { get; }

    public static Result<ImageId, Error> Create(Guid value)
    {
        if (value == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(value));

        return new ImageId(value);
    }
}
