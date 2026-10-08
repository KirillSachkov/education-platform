using CSharpFunctionalExtensions;
using SharedKernel;

namespace MaterialProcessingService.Domain.Transcripts.ValueObjects;

public sealed record TranscriptSegments
{
    private TranscriptSegments(IReadOnlyList<VideoTranscriptSegment> items) => Items = items;

    public IReadOnlyList<VideoTranscriptSegment> Items { get; }

    public static Result<TranscriptSegments, Error> Create(IReadOnlyList<VideoTranscriptSegment> items)
    {
        if (items.Count == 0)
        {
            return Error.Validation(
                "timecodes.transcript.empty",
                "Не удалось получить расшифровку видео");
        }

        return new TranscriptSegments(items
            .OrderBy(x => x.Range.StartSeconds)
            .ToArray());
    }
}
