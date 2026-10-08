using CSharpFunctionalExtensions;
using SharedKernel;

namespace MaterialProcessingService.Domain.Transcripts.ValueObjects;

public sealed record TranscriptDuration
{
    private TranscriptDuration(int seconds) => Seconds = seconds;

    public int Seconds { get; }

    public static Result<TranscriptDuration, Error> Create(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return GeneralErrors.ValueIsInvalid(nameof(TranscriptDuration));

        return new TranscriptDuration((int)Math.Ceiling(duration.TotalSeconds));
    }
}
