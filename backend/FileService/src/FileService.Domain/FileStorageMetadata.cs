namespace FileService.Domain;

public sealed class FileStorageMetadata
{
    public string? Checksum { get; init; }

    public string? ETag { get; init; }

    public string? DetectedContentType { get; init; }
}
