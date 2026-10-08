namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal sealed record TimecodeGenerationResponse(
    string Language,
    List<TimecodeItemResponse> Timecodes);
