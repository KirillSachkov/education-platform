using System.Diagnostics.Metrics;

namespace FileService.Core.Services;

public sealed class FileServiceMetrics
{
    private static readonly Meter METER = new("FileService");

    private readonly Counter<long> _pendingFileUploadStuckCounter =
        METER.CreateCounter<long>("fileservice_files_pending_upload_stuck_total");

    private readonly Counter<long> _staleDraftFileCounter =
        METER.CreateCounter<long>("fileservice_files_draft_stale_total");

    private readonly Counter<long> _deletingStuckCounter =
        METER.CreateCounter<long>("fileservice_assets_deleting_stuck_total");

    private readonly Counter<long> _videoReconciliationRepairedCounter =
        METER.CreateCounter<long>("fileservice_videos_reconciliation_repaired_total");

    private readonly Counter<long> _deleteRetryAttemptCounter =
        METER.CreateCounter<long>("fileservice_assets_delete_retry_attempt_total");

    public void RecordPendingFileUploadStuck(int count) => _pendingFileUploadStuckCounter.Add(count);

    public void RecordStaleDraftFile(int count) => _staleDraftFileCounter.Add(count);

    public void RecordDeletingStuck(int count) => _deletingStuckCounter.Add(count);

    public void RecordReconciliationRepaired(int count) => _videoReconciliationRepairedCounter.Add(count);

    public void RecordDeleteRetryAttempt() => _deleteRetryAttemptCounter.Add(1);
}
