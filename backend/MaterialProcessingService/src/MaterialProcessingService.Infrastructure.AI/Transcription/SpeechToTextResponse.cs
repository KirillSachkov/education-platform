namespace MaterialProcessingService.Infrastructure.AI.Transcription;

internal sealed record SpeechToTextResponse(
    string Language,
    List<SpeechToTextSegmentResponse> Segments);
