using Dapper;
using MaterialProcessingService.Core.Features.AdminInsights;
using Microsoft.EntityFrameworkCore;

namespace MaterialProcessingService.Infrastructure.Postgres.AdminInsights;

public sealed class AiUsageQueryService : IAiUsageQueryService
{
    private const string AGGREGATE_SQL = """
        with combined as (
          select
            'TIMECODES'::text as job_kind,
            coalesce(model_override, '<default>') as model,
            status,
            date_trunc('day', created_at) as day
          from material_processing.timecode_generation_jobs
          where created_at >= @since

          union all

          select
            'CONTENT'::text as job_kind,
            coalesce(model_override, '<default>') as model,
            status,
            date_trunc('day', created_at) as day
          from material_processing.content_generation_jobs
          where created_at >= @since
        )
        select
          to_char(day, 'YYYY-MM-DD') as "Day",
          job_kind as "JobKind",
          model as "Model",
          status as "Status",
          count(*)::int as "Count"
        from combined
        group by day, job_kind, model, status
        order by day desc, job_kind, model, status;
        """;

    private readonly MaterialProcessingServiceDbContext _db;

    public AiUsageQueryService(MaterialProcessingServiceDbContext db)
    {
        _db = db;
    }

    public async Task<AiUsageSnapshot> GetUsageAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var connection = _db.Database.GetDbConnection();

        var rows = await connection.QueryAsync<AiUsageRowDto>(
            new CommandDefinition(
                AGGREGATE_SQL,
                new { since = sinceUtc },
                cancellationToken: cancellationToken));

        int transcriptsTotal = await _db.VideoTranscripts
            .Where(t => t.CreatedAt >= sinceUtc)
            .CountAsync(cancellationToken);

        return new AiUsageSnapshot(transcriptsTotal, [.. rows]);
    }
}
