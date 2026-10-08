namespace MaterialProcessingService.Core.Features.Timecodes;

public sealed record GeneratedVideoTimecode(
    int StartSeconds,
    int? EndSeconds,
    string Title,
    double Confidence);
