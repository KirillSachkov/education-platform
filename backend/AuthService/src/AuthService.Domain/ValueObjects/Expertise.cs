namespace AuthService.Domain.ValueObjects;

/// <summary>Область экспертизы проверяющего.</summary>
public record Expertise
{
    public const int MAX_LENGTH = 500;

    private Expertise(string value) => Value = value;

    public string Value { get; }

    public static Result<Expertise, Error> Create(string value)
    {
        string normalized = value.Trim();

        if (normalized.Length > MAX_LENGTH)
            return GeneralErrors.ValueIsInvalid($"expertise (max {MAX_LENGTH} символов)");

        return new Expertise(normalized);
    }

    public static Result<Expertise?, Error> CreateOptional(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (Expertise?)null;

        Result<Expertise, Error> result = Create(raw);
        if (result.IsFailure)
            return result.Error;

        return result.Value;
    }
}
