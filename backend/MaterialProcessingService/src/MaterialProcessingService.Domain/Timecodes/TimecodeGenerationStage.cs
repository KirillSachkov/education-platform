namespace MaterialProcessingService.Domain.Timecodes;

public enum TimecodeGenerationStage
{
    Queued = 1,
    SourceFetch = 2,
    Probe = 3,
    AudioExtract = 4,
    Transcribe = 5,
    Generate = 6,
    Save = 7,
}
