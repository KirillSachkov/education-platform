using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using FileService.Core.FilesStorage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileService.Infrastructure.S3;

public class S3BucketInitializationService : BackgroundService
{
    private readonly FileStorageOptions _fileStorageOptions;
    private readonly ILogger<S3BucketInitializationService> _logger;
    private readonly IAmazonS3 _s3Client;

    public S3BucketInitializationService(
        IOptions<FileStorageOptions> s3Options,
        IAmazonS3 s3Client,
        ILogger<S3BucketInitializationService> logger)
    {
        _fileStorageOptions = s3Options.Value;
        _s3Client = s3Client;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_fileStorageOptions.BucketName))
            {
                _logger.LogInformation("S3 bucket initialization service requires bucket name");
                throw new InvalidOperationException("BucketName is required");
            }

            _logger.LogInformation(
                "Starting S3 bucket initialization for {Bucket}",
                _fileStorageOptions.BucketName);

            await InitializeBucketAsync(_fileStorageOptions.BucketName, stoppingToken);
            await EnsureCorsAsync(_fileStorageOptions.BucketName, stoppingToken);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "S3 bucket initialization service was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Critical error during S3 bucket initialization");
            throw;
        }
    }

    private async Task InitializeBucketAsync(string bucketName, CancellationToken cancellationToken)
    {
        try
        {
            bool bucketExists = await AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, bucketName);
            if (bucketExists)
            {
                _logger.LogInformation("Bucket {Bucket} already exists", bucketName);
                return;
            }

            _logger.LogInformation("Creating bucket '{BucketName}'", bucketName);

            PutBucketRequest putBucketRequest = new() { BucketName = bucketName };

            await _s3Client.PutBucketAsync(putBucketRequest, cancellationToken);

            _logger.LogInformation("Bucket '{BucketName}' created successfully", bucketName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize bucket '{BucketName}'", bucketName);
            throw;
        }
    }

    private async Task EnsureCorsAsync(string bucketName, CancellationToken cancellationToken)
    {
        if (_fileStorageOptions.CorsAllowedOrigins.Count == 0)
        {
            return;
        }

        try
        {
            CORSConfiguration corsConfig = new()
            {
                Rules =
                [
                    new CORSRule
                    {
                        AllowedOrigins = [.. _fileStorageOptions.CorsAllowedOrigins],
                        AllowedMethods = ["GET", "PUT", "HEAD"],
                        AllowedHeaders = ["*"],
                        ExposeHeaders = ["ETag"],
                        MaxAgeSeconds = 3600,
                    },
                ],
            };

            await _s3Client.PutCORSConfigurationAsync(
                new PutCORSConfigurationRequest
                {
                    BucketName = bucketName,
                    Configuration = corsConfig
                },
                cancellationToken);

            _logger.LogInformation(
                "CORS configured for bucket '{BucketName}' with origins: {Origins}",
                bucketName,
                string.Join(", ", _fileStorageOptions.CorsAllowedOrigins));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to configure CORS for bucket '{BucketName}'", bucketName);
        }
    }
}
