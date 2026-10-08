namespace EducationContentService.Domain.ValueObjects;

/// <summary>
///     Value Object — идентификатор видеозаписи.
///     Инвариант: не может быть <see cref="Guid.Empty"/>.
/// </summary>
public sealed record VideoId
{
    private VideoId(Guid value) => Value = value;

    public Guid Value { get; }

    public static Result<VideoId, Error> Create(Guid value)
    {
        if (value == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(value));

        return new VideoId(value);
    }
}
