namespace FileService.Infrastructure.Kinescope;

public sealed class KinescopeOptions
{
    public const string SECTION_NAME = "Kinescope";

    public string ApiToken { get; set; } = string.Empty;

    public string ParentId { get; set; } = string.Empty;

    public int RetryCount { get; set; } = 3;

    public double RetryBaseDelaySeconds { get; set; } = 1;

    public int TimeoutSeconds { get; set; } = 30;

    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    public string UploaderBaseUrl { get; set; } = "https://uploader.kinescope.io";

    public string ApiBaseUrl { get; set; } = "https://api.kinescope.io";

    public string EmbedBaseUrl { get; set; } = "https://kinescope.io";
}