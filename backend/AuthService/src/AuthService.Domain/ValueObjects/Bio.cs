namespace AuthService.Domain.ValueObjects;

/// <summary>Краткая биография пользователя.</summary>
public record Bio
{
    public const int MAX_LENGTH = 1000;

    private Bio(string value) => Value = value;

    public string Value { get; }

    public static Result<Bio, Error> Create(string value)
    {
        string normalized = value.Trim();

        if (normalized.Length > MAX_LENGTH)
            return GeneralErrors.ValueIsInvalid($"bio (max {MAX_LENGTH} символов)");

        return new Bio(normalized);
    }

    public static Result<Bio?, Error> CreateOptional(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (Bio?)null;

        Result<Bio, Error> result = Create(raw);
        if (result.IsFailure)
            return result.Error;

        return result.Value;
    }
}
