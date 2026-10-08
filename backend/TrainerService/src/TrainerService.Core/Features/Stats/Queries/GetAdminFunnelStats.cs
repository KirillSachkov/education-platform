using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Admin;

namespace TrainerService.Core.Features.Stats.Queries;

public sealed record GetAdminFunnelStatsQuery(int Days) : IQuery;

public sealed class GetAdminFunnelStatsEndpoint : IEndpoint
{
    public const int DEFAULT_DAYS = 30;
    public const int MIN_DAYS = 1;
    public const int MAX_DAYS = 365;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/admin/stats/funnel",
                async Task<EndpointResult<AdminFunnelStatsDto>> (
                    GetAdminFunnelStatsHandler handler,
                    CancellationToken cancellationToken,
                    int? days = null) =>
                    await handler.Handle(
                        new GetAdminFunnelStatsQuery(Math.Clamp(days ?? DEFAULT_DAYS, MIN_DAYS, MAX_DAYS)),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN)
            .RequireRateLimiting("admin-stats");
    }
}

/// <summary>
///     Admin-воронка тренажёра (#681 T3): старт→завершение (всего + по режиму), drop-off по позиции
///     вопроса и брошенные мок-собесы за окно <c>days</c> (clamp 1..365, default 30, по <c>started_at</c>).
///     Dapper raw-SQL агрегаты над <c>training_sessions</c> + <c>training_session_items</c>, без EF-загрузки —
///     зеркалит <c>GetAdminStats</c>.
///     <para>
///     <b>Drop-off по позиции:</b> для каждой ординальной позиции вопроса (<c>sort_index</c> в снапшоте
///     сессии) — сколько сессий ДОШЛО до неё (item выдан) и сколько ОТВЕТИЛО (<c>answered_at IS NOT NULL</c>).
///     Спад <c>Answered</c> по возрастающим позициям и есть «точка, где бросают». <c>sort_index</c> —
///     стабильный порядок вопроса в сессии (см. <c>TrainingSessionItem</c>).
///     </para>
///     <para>
///     <b>Брошенные мок-собесы:</b> MOCK-сессии, начатые в окне, но не <c>COMPLETED</c> (т.е. IN_PROGRESS
///     либо ABANDONED).
///     </para>
/// </summary>
public sealed class GetAdminFunnelStatsHandler
    : IQueryHandlerWithResult<AdminFunnelStatsDto, GetAdminFunnelStatsQuery>
{
    // All three modes always present (zero-filled) — mirrors GetAdminStats.ModeOrder.
    private static readonly string[] ModeOrder = ["DRILL", "LEARN", "MOCK"];

    private readonly ITransactionManager _transactions;

    public GetAdminFunnelStatsHandler(ITransactionManager transactions) => _transactions = transactions;

    public async Task<Result<AdminFunnelStatsDto, Error>> Handle(
        GetAdminFunnelStatsQuery query,
        CancellationToken cancellationToken)
    {
        DateTimeOffset cutoffUtc = DateTimeOffset.UtcNow.AddDays(-query.Days);

        DbConnection connection = _transactions.GetDbConnection();

        object args = new { Cutoff = cutoffUtc };

        // --- Start → complete, per mode (overall summed in C#). ---
        const string completionByModeSql = """
            SELECT
                mode                                         AS Mode,
                COUNT(*)::int                                     AS Started,
                (COUNT(*) FILTER (WHERE status = 'COMPLETED'))::int AS Completed
            FROM training_sessions
            WHERE started_at >= @Cutoff
            GROUP BY mode;
            """;

        // --- Drop-off by question position (ordinal sort_index within a session). ---
        const string dropOffSql = """
            SELECT
                i.sort_index                                       AS Position,
                COUNT(*)::int                                       AS Reached,
                (COUNT(*) FILTER (WHERE i.answered_at IS NOT NULL))::int  AS Answered
            FROM training_session_items i
            JOIN training_sessions s ON s.id = i.session_id
            WHERE s.started_at >= @Cutoff
            GROUP BY i.sort_index
            ORDER BY i.sort_index;
            """;

        // --- Abandoned mocks (MOCK started but not COMPLETED). ---
        const string abandonedMocksSql = """
            SELECT
                (COUNT(*) FILTER (WHERE mode = 'MOCK'))::int                           AS MockStarted,
                (COUNT(*) FILTER (WHERE mode = 'MOCK' AND status <> 'COMPLETED'))::int AS MockAbandoned
            FROM training_sessions
            WHERE started_at >= @Cutoff;
            """;

        IReadOnlyList<ModeCompletionRow> modeRows =
            (await connection.QueryAsync<ModeCompletionRow>(
                new CommandDefinition(completionByModeSql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<DropOffRow> dropOffRows =
            (await connection.QueryAsync<DropOffRow>(
                new CommandDefinition(dropOffSql, args, cancellationToken: cancellationToken))).ToList();

        AbandonedMocksRow abandoned = await connection.QuerySingleAsync<AbandonedMocksRow>(
            new CommandDefinition(abandonedMocksSql, args, cancellationToken: cancellationToken));

        long totalStarted = modeRows.Sum(r => r.Started);
        long totalCompleted = modeRows.Sum(r => r.Completed);

        AdminFunnelStatsDto dto = new(
            query.Days,
            new AdminCompletionDto(totalStarted, totalCompleted, Rate(totalCompleted, totalStarted)),
            BuildModeCompletion(modeRows),
            dropOffRows
                .Select(r => new AdminDropOffPositionDto(r.Position, r.Reached, r.Answered, Rate(r.Answered, r.Reached)))
                .ToList(),
            new AdminAbandonedMocksDto(
                abandoned.MockStarted,
                abandoned.MockAbandoned,
                Rate(abandoned.MockAbandoned, abandoned.MockStarted)));

        return dto;
    }

    private static double Rate(long numerator, long denominator) =>
        denominator == 0 ? 0d : (double)numerator / denominator;

    /// <summary>Все три режима (DRILL/LEARN/MOCK) всегда возвращаются, нулями при отсутствии данных.</summary>
    private static IReadOnlyList<AdminModeCompletionDto> BuildModeCompletion(IReadOnlyList<ModeCompletionRow> rows)
    {
        Dictionary<string, ModeCompletionRow> byMode =
            rows.ToDictionary(r => r.Mode, StringComparer.Ordinal);

        return ModeOrder
            .Select(mode =>
            {
                if (byMode.TryGetValue(mode, out ModeCompletionRow? row))
                    return new AdminModeCompletionDto(mode, row.Started, row.Completed, Rate(row.Completed, row.Started));

                return new AdminModeCompletionDto(mode, 0, 0, 0d);
            })
            .ToList();
    }

    // Count fields are int to match the `::int` SQL casts (Dapper maps int4 → int, not long).
    private sealed record ModeCompletionRow(string Mode, int Started, int Completed);

    private sealed record DropOffRow(int Position, int Reached, int Answered);

    private sealed record AbandonedMocksRow(int MockStarted, int MockAbandoned);
}
