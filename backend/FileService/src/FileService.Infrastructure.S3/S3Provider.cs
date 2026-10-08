using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using CSharpFunctionalExtensions;
using FileService.Core.FilesStorage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace FileService.Infrastructure.S3;

public sealed class S3Provider : IObjectStorageProvider
{
    private readonly FileStorageOptions _options;
    private readonly ILogger<S3Provider> _logger;
    private readonly IAmazonS3 _s3Client;

    public S3Provider(
        IAmazonS3 s3Client,
        IOptions<FileStorageOptions> options,
        ILogger<S3Provider> logger)
    {
        _s3Client = s3Client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<ObjectStorageUploadSession, Error>> InitiateUploadAsync(
        string objectKey,
        string contentType,
        long size,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string normalizedObjectKey = BuildObjectKey(objectKey);

            GetPreSignedUrlRequest request = new()
            {
                BucketName = _options.BucketName,
                Key = normalizedObjectKey,
                Verb = HttpVerb.PUT,
                ContentType = contentType,
                Expires = DateTime.UtcNow.AddMinutes(_options.UploadUrlExpirationMinutes),
                Protocol = _options.WithSsl ? Protocol.HTTPS : Protocol.HTTP,
            };
            request.Headers.ContentLength = size;

            string url = await _s3Client.GetPreSignedURLAsync(request);

            return new ObjectStorageUploadSession(
                normalizedObjectKey,
                url,
                new Dictionary<string, string> { ["Content-Type"] = contentType, });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate upload for object {ObjectKey}", objectKey);
            return S3ErrorMapper.ToError(ex);
        }
    }

    public async Task<Result<ObjectStorageObjectMetadata, Error>> GetMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string normalizedObjectKey = BuildObjectKey(objectKey);

            GetObjectMetadataResponse response = await _s3Client.GetObjectMetadataAsync(
                _options.BucketName,
                normalizedObjectKey,
                cancellationToken);

            return new ObjectStorageObjectMetadata(
                response.ContentLength,
                response.Headers.ContentType,
                response.ETag);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound ||
                                           string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.Ordinal))
        {
            return GeneralErrors.NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get object metadata for {ObjectKey}", objectKey);
            return S3ErrorMapper.ToError(ex);
        }
    }

    public async Task<Result<byte[], Error>> DownloadObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using GetObjectResponse response = await _s3Client.GetObjectAsync(
                _options.BucketName,
                BuildObjectKey(objectKey),
                cancellationToken);

            using var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound ||
                                           string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.Ordinal))
        {
            return GeneralErrors.NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download object {ObjectKey}", objectKey);
            return S3ErrorMapper.ToError(ex);
        }
    }

    public async Task<UnitResult<Error>> UploadObjectAsync(
        string objectKey,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var memoryStream = new MemoryStream(content, writable: false);
            await _s3Client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = BuildObjectKey(objectKey),
                    InputStream = memoryStream,
                    ContentType = contentType,
                    AutoCloseStream = false,
                },
                cancellationToken);

            return UnitResult.Success<Error>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload object {ObjectKey}", objectKey);
            return S3ErrorMapper.ToError(ex);
        }
    }

    public async Task<Result<string, Error>> GenerateDownloadUrlAsync(
        string objectKey,
        TimeSpan? expiration = null,
        string? contentType = null,
        string? downloadFileName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            TimeSpan lifetime = expiration ?? TimeSpan.FromMinutes(_options.DownloadUrlExpirationMinutes);

            // Прошиваем Content-Type и (опционально) Content-Disposition в подписанный
            // URL. Без явного Content-Type S3 на cross-origin redirect отдаёт
            // application/octet-stream — Firefox/Safari блокируют через
            // OpaqueResponseBlocking. downloadFileName != null означает «вложение» —
            // S3 отдаёт attachment с правильным filename. null означает inline для
            // картинок (avatar/preview/markdown image/etc.).
            ResponseHeaderOverrides overrides = new();
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                overrides.ContentType = contentType;
            }
            if (!string.IsNullOrWhiteSpace(downloadFileName))
            {
                overrides.ContentDisposition = BuildContentDisposition(downloadFileName);
            }

            GetPreSignedUrlRequest request = new()
            {
                BucketName = _options.BucketName,
                Key = BuildObjectKey(objectKey),
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(lifetime),
                Protocol = _options.WithSsl ? Protocol.HTTPS : Protocol.HTTP,
                ResponseHeaderOverrides = overrides,
            };

            string url = await _s3Client.GetPreSignedURLAsync(request);
            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate download URL for {ObjectKey}", objectKey);
            return S3ErrorMapper.ToError(ex);
        }
    }

    // RFC 6266 / 5987: ASCII fallback в filename="..." + UTF-8 percent-encoded в filename*
    // даёт кириллице и emoji корректное имя в Chrome/Firefox/Safari при скачивании.
    private static string BuildContentDisposition(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "attachment";
        }

        StringBuilder asciiFallback = new(fileName.Length);
        foreach (char c in fileName)
        {
            asciiFallback.Append(c is >= (char)0x20 and < (char)0x7F and not '"' and not '\\' ? c : '_');
        }

        string utf8Encoded = Uri.EscapeDataString(fileName);
        return $"attachment; filename=\"{asciiFallback}\"; filename*=UTF-8''{utf8Encoded}";
    }

    public async Task<UnitResult<Error>> DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _s3Client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = _options.BucketName, Key = BuildObjectKey(objectKey), },
                cancellationToken);

            return UnitResult.Success<Error>();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound ||
                                           string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.Ordinal))
        {
            return UnitResult.Success<Error>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete object {ObjectKey}", objectKey);
            return S3ErrorMapper.ToError(ex);
        }
    }

    private string BuildObjectKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("Object key must not be empty", nameof(objectKey));
        }

        string normalized = objectKey.Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(_options.KeyPrefix))
        {
            return normalized;
        }

        return $"{_options.KeyPrefix.Trim().Trim('/')}/{normalized}";
    }
}
