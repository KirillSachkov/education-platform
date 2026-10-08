using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Admin;
using TrainerService.Core.Database;
using TrainerService.Domain.Snapshots;

namespace TrainerService.Core.Features.Stats.Queries;

public sealed record GetAdminTrendsStatsQuery(int Days) : IQuery;

public sealed class GetAdminTrendsStatsEndpoint : IEndpoint
{
    public const int DEFAULT_DAYS = 30;
    public const int MIN_DAYS = 1;
    public const int MAX_DAYS = 365;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/admin/stats/trends",
                async Task<EndpointResult<AdminTrendStatsDto>> (
                    GetAdminTrendsStatsHandler handler,
                    CancellationToken cancellationToken,
                    int? days = null) =>
                    await handler.Handle(
                        new GetAdminTrendsStatsQuery(Math.Clamp(days ?? DEFAULT_DAYS, MIN_DAYS, MAX_DAYS)),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN)
            .RequireRateLimiting("admin-stats");
    }
}

/// <summary>
///     Owner-кривая динамики тренажёра по дням (#681 T6): плотный дневной ряд платформенных KPI за окно
///     <c>days</c> из снапшотов (<see cref="IStatSnapshotRepository.GetDailySeriesAsync"/>). Зеркалит
///     <c>BuildDenseDaily</c> в <c>GetAdminStats</c> — точка на КАЖДЫЙ день периода (cutoff-день .. сегодня
///     включительно = <c>days + 1</c> точек), пропуски занулены, чтобы тренд читался без разрывов.
///     Read-only поверх снапшотов; стоимость отдаётся в микрорублях (источник правды) + ₽.
/// </summary>
public sealed class GetAdminTrendsStatsHandler
    : IQueryHandlerWithResult<AdminTrendStatsDto, GetAdminTrendsStatsQuery>
{
    private readonly IStatSnapshotRepository _snapshots;

    public GetAdminTrendsStatsHandler(IStatSnapshotRepository snapshots) => _snapshots = snapshots;

    public async Task<Result<AdminTrendStatsDto, Error>> Handle(
        GetAdminTrendsStatsQuery query,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset cutoffUtc = nowUtc.AddDays(-query.Days);

        DateOnly startDay = DateOnly.FromDateTime(cutoffUtc.UtcDateTime.Date);
        DateOnly endDay = DateOnly.FromDateTime(nowUtc.UtcDateTime.Date);

        IReadOnlyList<DailyStatSnapshot> rows =
            await _snapshots.GetDailySeriesAsync(startDay, endDay, cancellationToken);
        Dictionary<DateOnly, DailyStatSnapshot> byDay = rows.ToDictionary(r => r.SnapshotDate);

        List<AdminTrendPointDto> points = new();
        for (DateOnly d = startDay; d <= endDay; d = d.AddDays(1))
        {
            points.Add(byDay.TryGetValue(d, out DailyStatSnapshot? snap)
                ? new AdminTrendPointDto(
                    d,
                    snap.SessionsStarted,
                    snap.ActiveUsers,
                    snap.CompletedSessions,
                    snap.TotalCostMicroRub,
                    snap.TotalCostMicroRub / 1_000_000m,
                    snap.AvgAccuracyPct)
                : new AdminTrendPointDto(d, 0, 0, 0, 0L, 0m, 0d));
        }

        return new AdminTrendStatsDto(query.Days, points);
    }
}
