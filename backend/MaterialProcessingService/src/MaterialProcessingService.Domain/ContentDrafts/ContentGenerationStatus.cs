namespace MaterialProcessingService.Domain.ContentDrafts;

public enum ContentGenerationStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
}
