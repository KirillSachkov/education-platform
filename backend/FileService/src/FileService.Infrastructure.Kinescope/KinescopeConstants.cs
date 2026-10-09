namespace FileService.Infrastructure.Kinescope;

/// <summary>
///     Constants used by Kinescope API integration.
/// </summary>
public static class KinescopeConstants
{
    /// <summary>
    ///     Media type for video uploads.
    /// </summary>
    public const string VIDEO_TYPE = "video";

    /// <summary>
    ///     Kinescope status indicating video is ready for playback.
    /// </summary>
    public const string STATUS_DONE = "done";

    /// <summary>
    ///     Kinescope status indicating video is still processing.
    /// </summary>
    public const string STATUS_PROCESSING = "processing";

    /// <summary>
    ///     Kinescope status indicating video processing failed.
    /// </summary>
    public const string STATUS_FAILED = "failed";

    /// <summary>
    ///     Kinescope webhook event type for status updates.
    /// </summary>
    public const string WEBHOOK_EVENT_STATUS_UPDATE = "media.update.status";
}