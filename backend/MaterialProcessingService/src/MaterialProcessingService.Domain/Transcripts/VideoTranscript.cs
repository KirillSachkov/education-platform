using CSharpFunctionalExtensions;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Transcripts.ValueObjects;
using SharedKernel;

namespace MaterialProcessingService.Domain.Transcripts;

public sealed class VideoTranscript
{
    private VideoTranscript()
    {
    }

    private VideoTranscript(
        Guid id,
        Guid videoAssetId,
        Guid assetVersion,
        TranscriptDuration duration,
        Language language,
        TranscriptSegments segments)
    {
        Id = id;
        VideoAssetId = videoAssetId;
        AssetVersion = assetVersion;
        Duration = duration;
        Language = language;
        Segments = segments;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public Guid VideoAssetId { get; private set; }

    public Guid AssetVersion { get; private set; }

    public TranscriptDuration Duration { get; private set; } = null!;

    public Language Language { get; private set; } = null!;

    public TranscriptSegments Segments { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<VideoTranscript, Error> Create(
        Guid videoAssetId,
        Guid assetVersion,
        TranscriptDuration duration,
        Language language,
        TranscriptSegments segments)
    {
        if (videoAssetId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(videoAssetId));

        if (assetVersion == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(assetVersion));

        return new VideoTranscript(
            Guid.CreateVersion7(),
            videoAssetId,
            assetVersion,
            duration,
            language,
            segments);
    }
}
