using System.Linq.Expressions;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.Transcripts;

namespace MaterialProcessingService.Infrastructure.Postgres.Repositories;

public sealed class VideoTranscriptRepository : IVideoTranscriptRepository
{
    private readonly MaterialProcessingServiceDbContext _context;
    private readonly ILogger<VideoTranscriptRepository> _logger;

    public VideoTranscriptRepository(
        MaterialProcessingServiceDbContext context,
        ILogger<VideoTranscriptRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task AddAsync(VideoTranscript transcript, CancellationToken cancellationToken = default)
    {
        await _context.VideoTranscripts.AddAsync(transcript, cancellationToken);
    }

    public async Task<VideoTranscript?> GetByVideoAssetVersionAsync(
        Guid videoAssetId,
        Guid assetVersion,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<VideoTranscript> query = _context.VideoTranscripts
            .Where(x => x.VideoAssetId == videoAssetId && x.AssetVersion == assetVersion)
            .OrderByDescending(x => x.CreatedAt);

        if (asNoTracking)
            query = query.AsNoTracking();

        try
        {
            return await query.FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex) when (IsTranscriptMaterializationFailure(ex))
        {
            _logger.LogWarning(ex, "Skipping invalid persisted transcript because it cannot be materialized");
            return null;
        }
    }

    public Task<int> DeleteByVideoAssetVersionAsync(
        Guid videoAssetId,
        Guid assetVersion,
        CancellationToken cancellationToken = default) =>
        _context.VideoTranscripts
            .Where(x => x.VideoAssetId == videoAssetId && x.AssetVersion == assetVersion)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetVideoIdsWithTranscriptAsync(
        IReadOnlyCollection<Guid> videoIds,
        Guid? requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (videoIds.Count == 0)
            return new HashSet<Guid>();

        List<Guid> rows = await _context.VideoTranscripts
            .AsNoTracking()
            .Where(x => videoIds.Contains(x.VideoAssetId)
                        && (!requestedByUserId.HasValue
                            || _context.TimecodeGenerationJobs.Any(job =>
                                job.VideoAssetId == x.VideoAssetId
                                && job.AssetVersion == x.AssetVersion
                                && job.RequestedByUserId == requestedByUserId.Value)
                            || _context.ContentGenerationJobs.Any(job =>
                                job.VideoAssetId == x.VideoAssetId
                                && job.AssetVersion == x.AssetVersion
                                && job.RequestedByUserId == requestedByUserId.Value)))
            .Select(x => x.VideoAssetId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows.ToHashSet();
    }

    public async Task<VideoTranscript?> GetByAsync(
        Expression<Func<VideoTranscript, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.VideoTranscripts
                .Where(predicate)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex) when (IsTranscriptMaterializationFailure(ex))
        {
            _logger.LogWarning(ex, "Skipping invalid persisted transcript because it cannot be materialized");
            return null;
        }
    }

    private static bool IsTranscriptMaterializationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ResultFailureException or System.Text.Json.JsonException)
                return true;
        }

        return false;
    }
}
