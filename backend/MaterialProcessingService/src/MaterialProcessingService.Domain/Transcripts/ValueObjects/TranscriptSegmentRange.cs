using CSharpFunctionalExtensions;
using SharedKernel;

namespace MaterialProcessingService.Domain.Transcripts.ValueObjects;

public sealed record TranscriptSegmentRange
{
    private TranscriptSegmentRange(double startSeconds, double endSeconds)
    {
        StartSeconds = startSeconds;
        EndSeconds = endSeconds;
    }

    public double StartSeconds { get; }

    public double EndSeconds { get; }

    public static Result<TranscriptSegmentRange, Error> Create(double startSeconds, double endSeconds)
    {
        if (startSeconds < 0 || endSeconds <= startSeconds)
            return GeneralErrors.ValueIsInvalid(nameof(TranscriptSegmentRange));

        return new TranscriptSegmentRange(startSeconds, endSeconds);
    }
}
