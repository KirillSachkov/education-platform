using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;

namespace ProgressService.Core.Features.Gamification.Queries;

/// <summary>
/// Запрос на получение сводки по XP текущего пользователя.
/// </summary>
public sealed record GetMyXpProgressQuery : IQuery;

/// <summary>
/// Публикует endpoint для чтения текущего XP и уровня пользователя.
/// </summary>
public sealed class GetMyXpProgressEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/me/gamification",
                async Task<EndpointResult<UserXpProgressResponse>> (
                        GetMyXpProgressHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMyXpProgressQuery(), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
/// Возвращает сводку по XP и уровням для текущего пользователя.
/// </summary>
public sealed class GetMyXpProgressHandler
    : IQueryHandlerWithResult<UserXpProgressResponse, GetMyXpProgressQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IXpLevelPolicy _xpLevelPolicy;

    public GetMyXpProgressHandler(
        ITransactionManager transactionManager,
        UserScopedData user,
        IXpLevelPolicy xpLevelPolicy)
    {
        _transactionManager = transactionManager;
        _user = user;
        _xpLevelPolicy = xpLevelPolicy;
    }

    /// <summary>
    /// Читает суммарный XP пользователя и вычисляет производные значения уровня из конфигурации.
    /// </summary>
    public async Task<Result<UserXpProgressResponse, Error>> Handle(
        GetMyXpProgressQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               ugs.total_xp AS TotalXp
                           FROM user_gamification_stats ugs
                           WHERE ugs.user_id = @UserId
                           LIMIT 1
                           """;

        UserXpProgressRow? row = await connection.QueryFirstOrDefaultAsync<UserXpProgressRow>(
            new CommandDefinition(
                sql,
                new { UserId = _user.UserId },
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

    /// <summary>
    /// Внутренняя проекция строки запроса к БД.
    /// </summary>
    private sealed class UserXpProgressRow
    {
        public int TotalXp { get; init; }
    }
}
