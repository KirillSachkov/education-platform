namespace FileService.Domain;

public readonly record struct MediaContentType
{
    private MediaContentType(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<MediaContentType, Error> Of(string value, string fieldName = "contentType")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GeneralErrors.ValueIsInvalid(fieldName);
        }

        string normalized = value.Trim().ToLowerInvariant();
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            return GeneralErrors.ValueIsInvalid(fieldName);
        }

        return new MediaContentType(normalized);
    }

    public bool StartsWith(string prefix) => Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Value;
}
