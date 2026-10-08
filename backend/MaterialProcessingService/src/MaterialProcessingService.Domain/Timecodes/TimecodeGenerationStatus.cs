namespace MaterialProcessingService.Domain.Timecodes;

public enum TimecodeGenerationStatus
{
    Queued = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
}
