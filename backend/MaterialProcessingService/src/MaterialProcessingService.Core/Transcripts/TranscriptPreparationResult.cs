namespace MaterialProcessingService.Core.Transcripts;

public sealed record TranscriptPreparationResult(
    Transcript Transcript,
    TimeSpan VideoDuration);
