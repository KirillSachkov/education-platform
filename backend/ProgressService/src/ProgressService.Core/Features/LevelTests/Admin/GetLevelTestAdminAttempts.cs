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

public sealed record GetLevelTestAdminAttemptsQuery(int Offset, int Limit) : IQuery;

public sealed record LevelTestAdminAttemptRow(
    Guid AttemptId,
    Guid? UserId,
    string? Username,
    string? DisplayName,
    int OverallPercent,
    string Level,
    int AnsweredCount,
    int TotalQuestions,
    string AiGradingStatus,
    bool IsClaimed,
    DateTime CreatedAt);

public sealed record LevelTestAdminAttemptsResponse(
    IReadOnlyList<LevelTestAdminAttemptRow> Items,
    long TotalCount,
    int Offset,
    int Limit);

public sealed class GetLevelTestAdminAttemptsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/level-test/admin/attempts",
                async Task<EndpointResult<LevelTestAdminAttemptsResponse>> (
                    [FromServices] GetLevelTestAdminAttemptsHandler handler,
                    [FromQuery] int? offset,
                    [FromQuery] int? limit,
                    CancellationToken ct) =>
                await handler.Handle(
                    new GetLevelTestAdminAttemptsQuery(
                        Math.Max(offset ?? 0, 0),
                        Math.Clamp(limit ?? 20, 1, 100)),
                    ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

/// <summary>
///     Список попыток level-test для админки (#537), новые первыми.
///     Имя юзера — из локальной проекции <c>progress_users</c> (без похода в
///     AuthService); аноним — обе колонки NULL. Offset-паджинация — админ-зона.
/// </summary>
public sealed class GetLevelTestAdminAttemptsHandler
    : IQueryHandlerWithResult<LevelTestAdminAttemptsResponse, GetLevelTestAdminAttemptsQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetLevelTestAdminAttemptsHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<LevelTestAdminAttemptsResponse, Error>> Handle(
        GetLevelTestAdminAttemptsQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string itemsSql = """
            SELECT
                a.id AS attempt_id,
                a.user_id,
                u.username,
                u.display_name,
                a.overall_percent,
                a.level,
                jsonb_array_length(a.answers) AS answered_count,
                jsonb_array_length(a.question_results) AS total_questions,
                a.ai_grading_status,
                (a.claimed_at IS NOT NULL OR a.user_id IS NOT NULL) AS is_claimed,
                a.created_at
            FROM level_test_attempts a
            LEFT JOIN progress_users u ON u.user_id = a.user_id
            ORDER BY a.created_at DESC
            LIMIT @Limit OFFSET @Offset;
            """;

        const string countSql = "SELECT COUNT(*) FROM level_test_attempts;";

        CommandDefinition itemsCommand = new(
            itemsSql,
            new { query.Limit, query.Offset },
            cancellationToken: cancellationToken);
        CommandDefinition countCommand = new(countSql, cancellationToken: cancellationToken);

        IEnumerable<LevelTestAdminAttemptRow> items =
            await connection.QueryAsync<LevelTestAdminAttemptRow>(itemsCommand);
        long total = await connection.ExecuteScalarAsync<long>(countCommand);

        return new LevelTestAdminAttemptsResponse(items.ToList(), total, query.Offset, query.Limit);
    }
}
