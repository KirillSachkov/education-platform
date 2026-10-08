using Amazon.S3;
using Amazon.S3.Model;
using FileService.Core.FilesStorage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FileService.Web.Configuration;

/// <summary>
///     File downloads and uploads depend on the object storage bucket being reachable with the configured credentials.
/// </summary>
public sealed class S3HealthCheck : IHealthCheck
{
    private readonly IAmazonS3 _s3Client;
    private readonly FileStorageOptions _options;

    public S3HealthCheck(IAmazonS3 s3Client, IOptions<FileStorageOptions> options)
    {
        _s3Client = s3Client;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _s3Client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = _options.BucketName,
                    MaxKeys = 1,
                },
                cancellationToken);

            return HealthCheckResult.Healthy("S3 bucket is reachable");
        }
        catch (AmazonS3Exception ex)
        {
            return HealthCheckResult.Unhealthy("S3 bucket probe failed", ex);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("S3 health check failed", ex);
        }
    }
}
