namespace AuthService.Domain.ValueObjects;

/// <summary>Описание автора как создателя курсов.</summary>
public record AboutAsAuthor
{
    public const int MAX_LENGTH = 2000;

    private AboutAsAuthor(string value) => Value = value;

    public string Value { get; }

    public static Result<AboutAsAuthor, Error> Create(string value)
    {
        string normalized = value.Trim();

        if (normalized.Length > MAX_LENGTH)
            return GeneralErrors.ValueIsInvalid($"about as author (max {MAX_LENGTH} символов)");

        return new AboutAsAuthor(normalized);
    }

    public static Result<AboutAsAuthor?, Error> CreateOptional(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (AboutAsAuthor?)null;

        Result<AboutAsAuthor, Error> result = Create(raw);
        if (result.IsFailure)
            return result.Error;

        return result.Value;
    }
}
