namespace FileService.Core.Services;

public sealed record VideoUploadInitResult(
    string ExternalAssetId,
    string UploadUrl);

public sealed record VideoProviderAssetInfo(
    string ExternalAssetId,
    string Title,
    string Status,
    string? ThumbnailUrl,
    double? Duration,
    int? Width,
    int? Height);

public sealed record VideoProviderChapter(
    string Id,
    string Title,
    double StartSeconds);

public interface IVideoProvider
{
    Task<Result<VideoUploadInitResult, Error>> InitiateUploadAsync(
        string title,
        string fileName,
        long fileSize,
        string? clientIp = null,
        CancellationToken cancellationToken = default);

    Task<Result<VideoProviderAssetInfo, Error>> GetStatusAsync(
        string externalAssetId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyDictionary<string, VideoProviderAssetInfo>, Error>> GetStatusesBatchAsync(
        IReadOnlyList<string> externalAssetIds,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<VideoProviderChapter>, Error>> GetChaptersAsync(
        string externalAssetId,
        CancellationToken cancellationToken = default);

    Task<UnitResult<Error>> ReplaceChaptersAsync(
        string externalAssetId,
        IReadOnlyList<Features.Videos.VideoChapterDefinition> chapters,
        CancellationToken cancellationToken = default);
}