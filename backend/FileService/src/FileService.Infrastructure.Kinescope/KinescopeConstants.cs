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

    /// <summary>
    ///     Processing source: Kinescope-encoded video asset (typically `mp4/original`)
    ///     fetched from AWS S3 us-west-1 via presigned URL. Heavy (full-resolution MP4),
    ///     cross-region from RU, subject to S3 burst rate-limit. Fallback only.
    /// </summary>
    public const string ProcessingSourceTypeProviderAsset = "PROVIDER_ASSET";

    /// <summary>
    ///     Processing source: Kinescope HLS manifest. Fallback when neither asset nor
    ///     audio-track is available.
    /// </summary>
    public const string ProcessingSourceTypeProviderHls = "PROVIDER_HLS";

    /// <summary>
    ///     Processing source: Kinescope `audio_tracks[].download_link` — audio-only MP4
    ///     fetched from Russian CDN (`ru-msk-dl-1.kinescopecdn.net`). ~10× smaller than
    ///     full video asset, faster, not rate-limited the same way as AWS S3. Preferred
    ///     for STT / timecode pipeline where video stream is unused.
    /// </summary>
    public const string ProcessingSourceTypeProviderAudioTrack = "PROVIDER_AUDIO_TRACK";
}