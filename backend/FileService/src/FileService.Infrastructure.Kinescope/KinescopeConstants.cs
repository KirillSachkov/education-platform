namespace FileService.Infrastructure.Kinescope;

/// <summary>
///     Constants used by Kinescope API integration.
/// </summary>
public static class KinescopeConstants
{
    /// <summary>
    ///     Media type for video uploads.
    /// </summary>
    public const string VideoType = "video";

    /// <summary>
    ///     Kinescope status indicating video is ready for playback.
    /// </summary>
    public const string StatusDone = "done";

    /// <summary>
    ///     Kinescope status indicating video is still processing.
    /// </summary>
    public const string StatusProcessing = "processing";

    /// <summary>
    ///     Kinescope status indicating video processing failed.
    /// </summary>
    public const string StatusFailed = "failed";

    /// <summary>
    ///     Kinescope webhook event type for status updates.
    /// </summary>
    public const string WebhookEventStatusUpdate = "media.update.status";
}