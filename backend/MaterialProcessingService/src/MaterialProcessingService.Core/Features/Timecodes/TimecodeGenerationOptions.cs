namespace MaterialProcessingService.Core.Features.Timecodes;

public sealed class TimecodeGenerationOptions
{
    public const string SECTION_NAME = "Timecodes";

    public int MinGapBetweenTimecodesSeconds { get; set; } = 45;
}
