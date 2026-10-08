namespace MaterialProcessingService.Core.Transcripts;

public sealed record TranscriptSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text);
