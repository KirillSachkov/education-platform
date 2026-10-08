namespace FileService.Domain;

public readonly record struct FileName
{
    public const int MAX_LENGTH = 500;

    private FileName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<FileName, Error> Of(string value, string fieldName = "fileName")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GeneralErrors.ValueIsInvalid(fieldName);
        }

        string normalized = value.Trim();
        if (normalized.Length > MAX_LENGTH)
        {
            return GeneralErrors.LengthIsInvalid(fieldName, max: MAX_LENGTH);
        }

        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return GeneralErrors.ValueIsInvalid(fieldName);
        }

        return new FileName(normalized);
    }

    public string GetExtension() => Path.GetExtension(Value).TrimStart('.').ToLowerInvariant();

    public override string ToString() => Value;
}
