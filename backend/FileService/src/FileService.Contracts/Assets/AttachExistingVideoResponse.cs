namespace FileService.Contracts.Assets;

public sealed record AttachExistingVideoResponse(
    Guid AssetId,
    string Status,
    string? ThumbnailUrl,
    double? DurationSeconds);
