using FileService.Contracts.Dtos;

namespace FileService.Contracts.Assets;

public sealed record GetVideoResponse(
    Guid Id,
    string Kind,
    string UsageType,
    string Status,
    string FileName,
    string ContentType,
    long Size,
    TargetEntityDto? TargetEntity,
    bool IsTemporary,
    string? ExternalVideoId,
    string? ThumbnailUrl,
    double? DurationSeconds);
