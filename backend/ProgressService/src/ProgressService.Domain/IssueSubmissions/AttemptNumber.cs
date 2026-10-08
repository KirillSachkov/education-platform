namespace ProgressService.Domain.IssueSubmissions;

/// <summary>
/// Порядковый номер попытки отправки решения по задаче.
/// Используется для упорядочивания повторных отправок и не допускает неположительных значений.
/// </summary>
public sealed record AttemptNumber
{
    private AttemptNumber(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static Result<AttemptNumber, Error> Create(int value)
    {
        if (value <= 0)
        {
            return ProgressErrors.AttemptNumberMustBePositive(nameof(value));
        }

        return new AttemptNumber(value);
    }
}
