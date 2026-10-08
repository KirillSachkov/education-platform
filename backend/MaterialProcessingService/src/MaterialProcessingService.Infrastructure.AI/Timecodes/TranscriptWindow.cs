using MaterialProcessingService.Core.Transcripts;

namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal sealed record TranscriptWindow(
    int StartSeconds,
    int EndSeconds,
    IReadOnlyList<TranscriptSegment> Segments);
