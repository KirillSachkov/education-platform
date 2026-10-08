namespace FileService.Domain;

public readonly record struct StorageKey
{
    public const int MAX_LENGTH = 500;
    private const string FILES_SEGMENT = "files";

    private StorageKey(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<StorageKey, Error> Of(string value, string fieldName = "storageKey")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GeneralErrors.ValueIsInvalid(fieldName);
        }

        string normalized = value.Trim().Replace('\\', '/').Trim('/');
        string[] segments =
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            return GeneralErrors.ValueIsInvalid(fieldName);
        }

        if (segments.Any(static segment => segment is "." or ".."))
        {
            return GeneralErrors.ValueIsInvalid(fieldName);
        }

        string key = string.Join('/', segments);
        if (key.Length > MAX_LENGTH)
        {
            return GeneralErrors.LengthIsInvalid(fieldName, max: MAX_LENGTH);
        }

        return new StorageKey(key);
    }

    public Result<StorageKey, Error> WithPrefix(string? prefix, string fieldName = "fileStorage.keyPrefix")
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return this;
        }

        Result<StorageKey, Error> prefixResult = Of(prefix, fieldName);
        if (prefixResult.IsFailure)
        {
            return prefixResult.Error;
        }

        return Of($"{prefixResult.Value.Value}/{Value}");
    }

    public static Result<StorageKey, Error> ForFile(Guid assetId, string canonicalExtension)
    {
        if (assetId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid("assetId");
        }

        if (string.IsNullOrWhiteSpace(canonicalExtension))
        {
            return GeneralErrors.ValueIsInvalid("canonicalExtension");
        }

        string normalizedExtension = canonicalExtension.Trim().TrimStart('.').ToLowerInvariant();
        return Of($"{FILES_SEGMENT}/{assetId:N}.{normalizedExtension}");
    }

    public override string ToString() => Value;
}
