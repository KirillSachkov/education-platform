namespace MaterialProcessingService.Infrastructure.AI.Transcription;

internal sealed record SpeechToTextSegmentResponse(
    double StartSeconds,
    double EndSeconds,
    string Text);
