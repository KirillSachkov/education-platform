namespace ProgressService.Domain.Issues;

/// <summary>
/// Значение с комментарием ревьюера по проверке решения задачи.
/// Инкапсулирует базовую валидацию текста feedback.
/// </summary>
public sealed record IssueReviewFeedback
{
    public const int MAX_LENGTH = 20000;

    private IssueReviewFeedback(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<IssueReviewFeedback, Error> Create(string? feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback))
        {
            return GeneralErrors.ValueIsInvalid(nameof(feedback));
        }

        string normalized = feedback.Trim();
        if (normalized.Length > MAX_LENGTH)
        {
            return ProgressErrors.ValueLengthExceeded(nameof(feedback), MAX_LENGTH);
        }

        return new IssueReviewFeedback(normalized);
    }
}
