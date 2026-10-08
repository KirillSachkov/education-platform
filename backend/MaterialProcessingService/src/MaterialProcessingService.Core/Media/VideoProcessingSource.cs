namespace MaterialProcessingService.Core.Media;

public sealed record VideoProcessingSource(
    Guid VideoId,
    Guid AssetVersion,
    double? DurationSeconds,
    string SourceType,
    string Url,
    DateTime ExpiresAt);
