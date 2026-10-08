using CSharpFunctionalExtensions;
using FileService.Core.FilesStorage;
using SharedKernel;

namespace FileService.Infrastructure.S3;

/// <summary>
/// Decorator that rewrites presigned URLs from the internal S3 endpoint to an external proxy URL.
/// Used in local dev where MinIO is behind nginx (minio:9000 → localhost/storage).
/// </summary>
public sealed class ProxiedObjectStorageProvider : IObjectStorageProvider
{
    private readonly IObjectStorageProvider _inner;
    private readonly string _internalEndpoint;
    private readonly string _externalEndpoint;

    public ProxiedObjectStorageProvider(
        IObjectStorageProvider inner,
        string internalEndpoint,
        string externalEndpoint)
    {
        _inner = inner;
        _internalEndpoint = internalEndpoint;
        _externalEndpoint = externalEndpoint;
    }

    public async Task<Result<ObjectStorageUploadSession, Error>> InitiateUploadAsync(
        string objectKey,
        string contentType,
        long size,
        CancellationToken cancellationToken = default)
    {
        Result<ObjectStorageUploadSession, Error> result =
            await _inner.InitiateUploadAsync(objectKey, contentType, size, cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        ObjectStorageUploadSession session = result.Value;

        return new ObjectStorageUploadSession(
            session.ObjectKey,
            RewriteUrl(session.UploadUrl),
            session.RequiredHeaders);
    }

    public Task<Result<ObjectStorageObjectMetadata, Error>> GetMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetMetadataAsync(objectKey, cancellationToken);
    }

    public async Task<Result<string, Error>> GenerateDownloadUrlAsync(
        string objectKey,
        TimeSpan? expiration = null,
        string? contentType = null,
        string? downloadFileName = null,
        CancellationToken cancellationToken = default)
    {
        Result<string, Error> result =
            await _inner.GenerateDownloadUrlAsync(
                objectKey,
                expiration,
                contentType,
                downloadFileName,
                cancellationToken);

        return result.IsSuccess
            ? RewriteUrl(result.Value)
            : result;
    }

    public Task<Result<byte[], Error>> DownloadObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        // Server-side direct call — no presigned URL to rewrite.
        return _inner.DownloadObjectAsync(objectKey, cancellationToken);
    }

    public Task<UnitResult<Error>> UploadObjectAsync(
        string objectKey,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        // Server-side direct call — no presigned URL to rewrite.
        return _inner.UploadObjectAsync(objectKey, content, contentType, cancellationToken);
    }

    public Task<UnitResult<Error>> DeleteAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        return _inner.DeleteAsync(objectKey, cancellationToken);
    }

    private string RewriteUrl(string presignedUrl)
    {
        return presignedUrl.Replace(
            _internalEndpoint,
            _externalEndpoint,
            StringComparison.OrdinalIgnoreCase);
    }
}
