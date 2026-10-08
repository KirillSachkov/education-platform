namespace AuthService.Domain.ValueObjects;

/// <summary>Специализация автора курсов.</summary>
public record Specialization
{
    public const int MAX_LENGTH = 255;

    private Specialization(string value) => Value = value;

    public string Value { get; }

    public static Result<Specialization, Error> Create(string value)
    {
        string normalized = value.Trim();

        if (normalized.Length > MAX_LENGTH)
            return GeneralErrors.ValueIsInvalid($"specialization (max {MAX_LENGTH} символов)");

        return new Specialization(normalized);
    }

    public static Result<Specialization?, Error> CreateOptional(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (Specialization?)null;

        Result<Specialization, Error> result = Create(raw);
        if (result.IsFailure)
            return result.Error;

        return result.Value;
    }
}
