namespace FileService.Core;

public sealed class FileMaintenanceOptions
{
    public int PendingUploadTtlMinutes { get; init; } = 60;

    public int DraftAssetTtlHours { get; init; } = 24;

    public int PendingUploadCleanupIntervalMinutes { get; init; } = 15;

    public int DraftCleanupIntervalMinutes { get; init; } = 60;

    public int PendingUploadBatchSize { get; init; } = 100;

    public int DraftCleanupBatchSize { get; init; } = 100;

    public int OrphanDraftTtlDays { get; init; } = 7;

    public int OrphanDraftCleanupIntervalMinutes { get; init; } = 360;

    public int OrphanDraftCleanupBatchSize { get; init; } = 200;
}
