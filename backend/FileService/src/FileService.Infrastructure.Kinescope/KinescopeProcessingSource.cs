namespace FileService.Infrastructure.Kinescope;

internal sealed record KinescopeProcessingSource(
    string SourceType,
    string Url,
    DateTime ExpiresAt);
