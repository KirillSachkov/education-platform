namespace FileService.Contracts.Assets;

public sealed record GetPublicVideoResponse(
    Guid Id,
    string? ThumbnailUrl,
    double? DurationSeconds);
