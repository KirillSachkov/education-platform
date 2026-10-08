using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Features.Courses.Queries;

namespace ProgressService.Core.Features.Gamification.Queries;

/// <summary>
/// Запрос на получение публичной XP-сводки конкретного пользователя.
/// Используется на страницах публичных профилей.
/// </summary>
public sealed record GetUserXpProgressQuery(Guid UserId) : IQuery;

/// <summary>
/// Публикует endpoint для чтения XP/уровня любого пользователя.
/// Доступен анонимам — данные те же, что показываются в leaderboard.
/// </summary>
public sealed class GetUserXpProgressEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/users/{userId:guid}/gamification",
                async Task<EndpointResult<UserXpProgressResponse>> (
                        [FromRoute] Guid userId,
                        GetUserXpProgressHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new GetUserXpProgressQuery(userId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(GetCoursePublicStatsEndpoint.ANONYMOUS_READ_RATE_LIMIT_POLICY);
    }
}

/// <summary>
/// Возвращает XP/уровень для указанного пользователя.
/// Если у пользователя нет записи — возвращает level=1, total_xp=0 (initial state).
/// </summary>
public sealed class GetUserXpProgressHandler
    : IQueryHandlerWithResult<UserXpProgressResponse, GetUserXpProgressQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IXpLevelPolicy _xpLevelPolicy;

    public GetUserXpProgressHandler(
        ITransactionManager transactionManager,
        IXpLevelPolicy xpLevelPolicy)
    {
        _transactionManager = transactionManager;
        _xpLevelPolicy = xpLevelPolicy;
    }

    public async Task<Result<UserXpProgressResponse, Error>> Handle(
        GetUserXpProgressQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT ugs.total_xp AS TotalXp
                           FROM user_gamification_stats ugs
                           WHERE ugs.user_id = @UserId
                           LIMIT 1
                           """;

        UserXpProgressRow? row = await connection.QueryFirstOrDefaultAsync<UserXpProgressRow>(
            new CommandDefinition(
                sql,
                new { UserId = query.UserId },
                cancellationToken: cancellationToken));

        int totalXp = row?.TotalXp ?? 0;
        int currentLevel = _xpLevelPolicy.ResolveLevel(totalXp);
        int? nextLevel = _xpLevelPolicy.ResolveNextLevel(totalXp);
        int? xpToNextLevel = _xpLevelPolicy.ResolveXpToNextLevel(totalXp);

        return new UserXpProgressResponse(
            totalXp,
            currentLevel,
            nextLevel,
            xpToNextLevel);
    }

    private sealed class UserXpProgressRow
    {
        public int TotalXp { get; init; }
    }
}
