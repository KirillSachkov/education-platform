namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal sealed record TimecodeItemResponse(
    int StartSeconds,
    int? EndSeconds,
    string Title,
    double Confidence);
