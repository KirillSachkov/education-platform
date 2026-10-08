namespace FileService.Domain;

public sealed class VideoProviderMetadata
{
    public string? ThumbnailUrl { get; init; }

    public double? DurationSeconds { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }
}
