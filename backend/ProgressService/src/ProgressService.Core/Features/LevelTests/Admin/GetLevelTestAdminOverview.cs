using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace ProgressService.Core.Features.LevelTests.Admin;

public sealed record GetLevelTestAdminOverviewQuery : IQuery;

public sealed record LevelTestLevelCountRow(string Level, long Count);

public sealed record LevelTestSectionAverageRow(
    string Key,
    string Title,
    double AveragePercent,
    long Attempts);

public sealed record LevelTestDayCountRow(DateTime Day, long Count);

public sealed record LevelTestAdminOverviewResponse(
    long TotalAttempts,
    long UserAttempts,
    long UniqueUsers,
    long AnonymousAttempts,
    long AttemptsLast7Days,
    double AveragePercent,
    IReadOnlyList<LevelTestLevelCountRow> LevelDistribution,
    IReadOnlyList<LevelTestSectionAverageRow> SectionAverages,
    IReadOnlyList<LevelTestDayCountRow> AttemptsByDay);

public sealed class GetLevelTestAdminOverviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/level-test/admin/overview",
                async Task<EndpointResult<LevelTestAdminOverviewResponse>> (
                    [FromServices] GetLevelTestAdminOverviewHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetLevelTestAdminOverviewQuery(), ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

/// <summary>
///     Админ-аналитика воронки level-test (#537): счётчики попыток, распределение
///     по уровням, средние по секциям (JSONB-снапшоты <c>section_scores</c>,
///     PascalCase-ключи) и динамика за 14 дней. Read-only Dapper.
/// </summary>
public sealed class GetLevelTestAdminOverviewHandler
    : IQueryHandlerWithResult<LevelTestAdminOverviewResponse, GetLevelTestAdminOverviewQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetLevelTestAdminOverviewHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<LevelTestAdminOverviewResponse, Error>> Handle(
        GetLevelTestAdminOverviewQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string countsSql = """
            SELECT
                (SELECT COUNT(*) FROM level_test_attempts) AS total_attempts,
                (SELECT COUNT(*) FROM level_test_attempts WHERE user_id IS NOT NULL) AS user_attempts,
                (SELECT COUNT(DISTINCT user_id) FROM level_test_attempts WHERE user_id IS NOT NULL) AS unique_users,
                (SELECT COUNT(*) FROM level_test_attempts WHERE user_id IS NULL) AS anonymous_attempts,
                (SELECT COUNT(*) FROM level_test_attempts WHERE created_at >= NOW() - INTERVAL '7 days') AS attempts_last7_days,
                (SELECT COALESCE(AVG(overall_percent), 0) FROM level_test_attempts) AS average_percent;
            """;

        const string levelsSql = """
            SELECT level, COUNT(*) AS count
            FROM level_test_attempts
            GROUP BY level;
            """;

        // section_scores — снапшот на момент попытки: ключи/названия секций берём
        // из самих снапшотов, поэтому статистика честна и для старых попыток,
        // даже если банк с тех пор пересеян.
        const string sectionsSql = """
            SELECT
                s->>'Key' AS key,
                MAX(s->>'Title') AS title,
                CAST(AVG((s->>'Percent')::int) AS float8) AS average_percent,
                COUNT(*) AS attempts
            FROM level_test_attempts,
                 jsonb_array_elements(section_scores) AS s
            GROUP BY s->>'Key'
            ORDER BY average_percent;
            """;

        const string byDaySql = """
            SELECT date_trunc('day', created_at) AS day, COUNT(*) AS count
            FROM level_test_attempts
            WHERE created_at >= NOW() - INTERVAL '14 days'
            GROUP BY day
            ORDER BY day;
            """;

        CommandDefinition countsCommand = new(countsSql, cancellationToken: cancellationToken);
        CommandDefinition levelsCommand = new(levelsSql, cancellationToken: cancellationToken);
        CommandDefinition sectionsCommand = new(sectionsSql, cancellationToken: cancellationToken);
        CommandDefinition byDayCommand = new(byDaySql, cancellationToken: cancellationToken);

        CountsRow counts = await connection.QuerySingleAsync<CountsRow>(countsCommand);
        IEnumerable<LevelTestLevelCountRow> levels =
            await connection.QueryAsync<LevelTestLevelCountRow>(levelsCommand);
        IEnumerable<LevelTestSectionAverageRow> sections =
            await connection.QueryAsync<LevelTestSectionAverageRow>(sectionsCommand);
        IEnumerable<LevelTestDayCountRow> byDay =
            await connection.QueryAsync<LevelTestDayCountRow>(byDayCommand);

        return new LevelTestAdminOverviewResponse(
            counts.TotalAttempts,
            counts.UserAttempts,
            counts.UniqueUsers,
            counts.AnonymousAttempts,
            counts.AttemptsLast7Days,
            Math.Round(counts.AveragePercent, 1),
            levels.ToList(),
            sections.ToList(),
            byDay.ToList());
    }

    private sealed record CountsRow
    {
        public long TotalAttempts { get; init; }
        public long UserAttempts { get; init; }
        public long UniqueUsers { get; init; }
        public long AnonymousAttempts { get; init; }
        public long AttemptsLast7Days { get; init; }
        public double AveragePercent { get; init; }
    }
}
