namespace MaterialProcessingService.Domain.ContentDrafts;

public enum ContentGenerationStage
{
    Queued = 0,
    SourceFetch = 1,
    Probe = 2,
    AudioExtract = 3,
    Transcribe = 4,
    Generate = 5,
    Save = 6,
}
