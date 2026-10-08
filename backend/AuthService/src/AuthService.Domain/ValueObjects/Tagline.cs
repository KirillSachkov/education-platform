namespace AuthService.Domain.ValueObjects;

/// <summary>Короткое описание / подзаголовок пространства автора.</summary>
public record Tagline
{
    public const int MAX_LENGTH = 500;

    private Tagline(string value) => Value = value;

    public string Value { get; }

    public static Result<Tagline, Error> Create(string value)
    {
        string normalized = value.Trim();

        if (normalized.Length > MAX_LENGTH)
            return GeneralErrors.ValueIsInvalid($"tagline (max {MAX_LENGTH} символов)");

        return new Tagline(normalized);
    }

    public static Result<Tagline?, Error> CreateOptional(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (Tagline?)null;

        Result<Tagline, Error> result = Create(raw);
        if (result.IsFailure)
            return result.Error;

        return result.Value;
    }
}
