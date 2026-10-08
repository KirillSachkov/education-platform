using MaterialProcessingService.Core.Cleanup;
using Microsoft.EntityFrameworkCore;

namespace MaterialProcessingService.Infrastructure.Postgres.Cleanup;

public sealed class MaterialProcessingCleanup : IMaterialProcessingCleanup
{
    private readonly MaterialProcessingServiceDbContext _db;

    public MaterialProcessingCleanup(MaterialProcessingServiceDbContext db)
    {
        _db = db;
    }

    public async Task<int> DeleteByVideoAssetIdAsync(
        Guid videoAssetId,
        CancellationToken cancellationToken = default)
    {
        int contentDeleted = await _db.ContentGenerationJobs
            .Where(j => j.VideoAssetId == videoAssetId)
            .ExecuteDeleteAsync(cancellationToken);

        int timecodeDeleted = await _db.TimecodeGenerationJobs
            .Where(j => j.VideoAssetId == videoAssetId)
            .ExecuteDeleteAsync(cancellationToken);

        int transcriptsDeleted = await _db.VideoTranscripts
            .Where(t => t.VideoAssetId == videoAssetId)
            .ExecuteDeleteAsync(cancellationToken);

        return contentDeleted + timecodeDeleted + transcriptsDeleted;
    }

    public Task<int> DeleteContentJobsByMaterialIdAsync(
        Guid materialId,
        CancellationToken cancellationToken = default) =>
        _db.ContentGenerationJobs
            .Where(j => j.MaterialId == materialId)
            .ExecuteDeleteAsync(cancellationToken);
}
