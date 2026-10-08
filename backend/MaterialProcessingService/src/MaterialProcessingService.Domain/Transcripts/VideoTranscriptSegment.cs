using CSharpFunctionalExtensions;
using SharedKernel;
using MaterialProcessingService.Domain.Transcripts.ValueObjects;

namespace MaterialProcessingService.Domain.Transcripts;

public sealed record VideoTranscriptSegment
{
    private VideoTranscriptSegment(TranscriptSegmentRange range, TranscriptSegmentText text)
    {
        Range = range;
        Text = text;
    }

    public TranscriptSegmentRange Range { get; }

    public TranscriptSegmentText Text { get; }

    public static Result<VideoTranscriptSegment, Error> Create(
        TranscriptSegmentRange range,
        TranscriptSegmentText text) =>
        new VideoTranscriptSegment(range, text);
}
