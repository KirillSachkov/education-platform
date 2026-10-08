using CSharpFunctionalExtensions;
using SharedKernel;
using MaterialProcessingService.Core.Media;

namespace MaterialProcessingService.Core.Transcripts;

public interface ISpeechToTextProvider
{
    Task<Result<SpeechToTextResult, Error>> TranscribeAsync(
        AudioChunk chunk,
        string? modelOverride,
        CancellationToken cancellationToken);
}

public sealed record SpeechToTextResult(
    string Language,
    IReadOnlyList<TranscriptSegment> Segments,
    SpeechTimestampSource TimestampSource);

public enum SpeechTimestampSource
{
    Model = 1,
    Estimated = 2,
}
