namespace FileService.Domain;

public sealed class AssetUsagePolicy
{
    private readonly IReadOnlyDictionary<string, string> _canonicalExtensions;
    private readonly HashSet<string> _allowedTargetTypes;

    public AssetUsagePolicy(
        AssetKind kind,
        AssetRegistrationMode registrationMode,
        long maxSize,
        IReadOnlyCollection<string> allowedContentTypes,
        IReadOnlyDictionary<string, string> canonicalExtensions,
        IReadOnlyCollection<string> allowedTargetTypes)
    {
        Kind = kind;
        RegistrationMode = registrationMode;
        MaxSize = maxSize;
        AllowedContentTypes = allowedContentTypes
            .Select(static x => x.Trim().ToLowerInvariant())
            .ToArray();
        _canonicalExtensions = canonicalExtensions.ToDictionary(
            static x => x.Key.Trim().ToLowerInvariant(),
            static x => x.Value.Trim().TrimStart('.').ToLowerInvariant(),
            StringComparer.OrdinalIgnoreCase);
        _allowedTargetTypes = allowedTargetTypes
            .Select(static x => x.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public AssetKind Kind { get; }

    public AssetRegistrationMode RegistrationMode { get; }

    public long MaxSize { get; }

    public IReadOnlyCollection<string> AllowedContentTypes { get; }

    public bool RequiresDraft => RegistrationMode is AssetRegistrationMode.DraftRequired or AssetRegistrationMode.DraftOrEntity;

    public bool IsTargetTypeAllowed(string targetType) => _allowedTargetTypes.Contains(targetType);

    public Result<string, Error> GetCanonicalExtension(MediaContentType contentType)
    {
        if (_canonicalExtensions.TryGetValue(contentType.Value, out string? canonicalExtension))
        {
            return canonicalExtension;
        }

        return Error.Validation("contentType.invalid", "Неподдерживаемый тип контента");
    }

    public UnitResult<Error> ValidateUpload(
        FileName fileName,
        MediaContentType contentType,
        long size)
    {
        if (size <= 0 || size > MaxSize)
        {
            return GeneralErrors.ValueIsInvalid("size");
        }

        string normalizedContentType = contentType.Value.Trim().ToLowerInvariant();
        if (!AllowedContentTypes.Contains(normalizedContentType))
        {
            return Error.Validation("contentType.invalid", "Неподдерживаемый тип контента");
        }

        Result<string, Error> canonicalExtensionResult = GetCanonicalExtension(contentType);
        if (canonicalExtensionResult.IsFailure)
        {
            return canonicalExtensionResult.Error;
        }

        string actualExtension = fileName.GetExtension();
        string canonicalExtension = canonicalExtensionResult.Value;

        if (Kind == AssetKind.FILE)
        {
            if (actualExtension is "jpeg")
            {
                actualExtension = "jpg";
            }

            if (!string.Equals(actualExtension, canonicalExtension, StringComparison.OrdinalIgnoreCase))
            {
                return Error.Validation("fileName.invalid.extension", "Расширение файла не соответствует типу контента");
            }
        }
        else
        {
            string[] allowedExtensions = normalizedContentType switch
            {
                "video/mp4" => ["mp4"],
                "video/webm" => ["webm"],
                "video/quicktime" => ["mov"],
                "video/x-matroska" => ["mkv"],
                "video/avi" => ["avi"],
                _ => [],
            };

            if (allowedExtensions.Length > 0 &&
                !allowedExtensions.Contains(actualExtension, StringComparer.OrdinalIgnoreCase))
            {
                return Error.Validation("fileName.invalid.extension", "Расширение файла не соответствует типу контента");
            }
        }

        return UnitResult.Success<Error>();
    }
}
