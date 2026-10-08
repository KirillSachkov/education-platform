namespace FileService.Core;

public sealed class AssetRetentionOptions
{
    public int DeleteRetryIntervalMinutes { get; init; } = 5;

    public int DeleteTtlHours { get; init; } = 24;

    public int TombstoneRetentionHours { get; init; } = 72;

    public int DeleteRetryBatchSize { get; init; } = 100;

    public int PurgeBatchSize { get; init; } = 100;

    public int RetentionSweepIntervalMinutes { get; init; } = 15;
}
