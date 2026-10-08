namespace EducationContentService.Domain.Projects.ValueObjects;

/// <summary>
///     Value Object — максимальный балл за задачу.
///     Инвариант: строго положительное число.
/// </summary>
public sealed record MaxScore
{
    private MaxScore(int value) => Value = value;

    public int Value { get; }

    public static Result<MaxScore, Error> Create(int value)
    {
        if (value <= 0)
        {
            return GeneralErrors.ValueIsInvalid(nameof(value));
        }

        return new MaxScore(value);
    }
}
