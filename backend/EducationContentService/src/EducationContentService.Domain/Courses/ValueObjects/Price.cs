namespace EducationContentService.Domain.Courses.ValueObjects;

/// <summary>
///     Value Object — цена курса.
///     Инвариант: не может быть отрицательной.
/// </summary>
public sealed record Price
{
    private Price(decimal value) => Value = value;

    public decimal Value { get; }

    public static Result<Price, Error> Create(decimal value)
    {
        if (value < 0)
        {
            return GeneralErrors.ValueIsInvalid(nameof(value));
        }

        return new Price(value);
    }
}
