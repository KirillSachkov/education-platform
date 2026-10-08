namespace MaterialProcessingService.Core.Transcripts;

public sealed record Transcript(
    string Language,
    IReadOnlyList<TranscriptSegment> Segments);
