namespace FileService.Contracts.Assets;

public sealed record GetVideoProcessingSourceResponse(
    Guid VideoId,
    string Status,
    Guid AssetVersion,
    double? DurationSeconds,
    string SourceType,
    string Url,
    DateTime ExpiresAt,
    Guid? UploadedByUserId = null);
