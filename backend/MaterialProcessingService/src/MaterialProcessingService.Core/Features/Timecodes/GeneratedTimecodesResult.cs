namespace MaterialProcessingService.Core.Features.Timecodes;

public sealed record GeneratedTimecodesResult(
    string Language,
    IReadOnlyList<GeneratedVideoTimecode> Timecodes);
