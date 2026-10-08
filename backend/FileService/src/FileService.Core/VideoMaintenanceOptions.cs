namespace FileService.Core;

public sealed class VideoMaintenanceOptions
{
    public int VideoUploadTimeoutHours { get; init; } = 24;

    public int VideoProcessingTimeoutHours { get; init; } = 6;

    public int VideoReconciliationIntervalMinutes { get; init; } = 5;

    public int ReconciliationBatchSize { get; init; } = 100;
}
