namespace FileService.Core.FilesStorage;

public sealed record ObjectStorageUploadSession(
    string ObjectKey,
    string UploadUrl,
    IReadOnlyDictionary<string, string> RequiredHeaders);

public sealed record ObjectStorageObjectMetadata(
    long Size,
    string? ContentType,
    string? ETag);

public interface IObjectStorageProvider
{
    Task<Result<ObjectStorageUploadSession, Error>> InitiateUploadAsync(
        string objectKey,
        string contentType,
        long size,
        CancellationToken cancellationToken = default);

    Task<Result<ObjectStorageObjectMetadata, Error>> GetMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Downloads the full object bytes. Used server-side for image variant
    ///     generation (issue #646) — NOT on any hot request path.
    /// </summary>
    Task<Result<byte[], Error>> DownloadObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Uploads (overwrites) an object directly from server-held bytes. Used to
    ///     store generated image variants (issue #646).
    /// </summary>
    Task<UnitResult<Error>> UploadObjectAsync(
        string objectKey,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Result<string, Error>> GenerateDownloadUrlAsync(
        string objectKey,
        TimeSpan? expiration = null,
        string? contentType = null,
        string? downloadFileName = null,
        CancellationToken cancellationToken = default);

    Task<UnitResult<Error>> DeleteAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
}
