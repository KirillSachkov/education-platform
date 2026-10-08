namespace FileService.Core.FilesStorage;

public record FileStorageOptions
{
    public string Endpoint { get; init; } = string.Empty;

    public string? ExternalEndpoint { get; set; }

    public string BucketName { get; init; } = "media";

    public string KeyPrefix { get; init; } = string.Empty;

    public string AccessKey { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    public bool WithSsl { get; init; }

    public bool ForcePathStyle { get; init; } = true;

    public IReadOnlyList<string> CorsAllowedOrigins { get; init; } = [];

    public int UploadUrlExpirationMinutes { get; init; } = 60;

    public int DownloadUrlExpirationMinutes { get; init; } = 10080;

    public int ProtectedDownloadUrlExpirationMinutes { get; init; } = 15;

    public int MaxConcurrentRequests { get; init; } = 20;
}
