using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Courses.Queries;

/// <summary>
///     Агрегированные счётчики опубликованного контента платформы для маркетинговой
///     статистики на карточке «Полный доступ» (/pricing). Полный доступ открывает всё
///     опубликованное, поэтому считаем по всему PUBLISHED-контенту. Анонимный,
///     user-agnostic, кешируется (HybridCache).
/// </summary>
public sealed record GetPlatformContentStatsQuery : IQuery;

/// <summary>Счётчики контента в полном доступе. Frontend-only read-DTO.</summary>
public sealed record PlatformContentStatsDto(
    int CoursesCount,
    int CollectionsCount,
    int MaterialsCount,
    int IssuesCount);

public sealed class GetPlatformContentStatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/platform-stats", async Task<EndpointResult<PlatformContentStatsDto>> (
                    [FromServices] GetPlatformContentStatsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetPlatformContentStatsQuery(), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetPlatformContentStatsHandler
    : IQueryHandler<PlatformContentStatsDto, GetPlatformContentStatsQuery>
{
    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(2),
    };

    private readonly ITransactionManager _transactionManager;
    private readonly HybridCache _cache;

    public GetPlatformContentStatsHandler(ITransactionManager transactionManager, HybridCache cache)
    {
        _transactionManager = transactionManager;
        _cache = cache;
    }

    public async Task<PlatformContentStatsDto> Handle(
        GetPlatformContentStatsQuery query, CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync(
            "platform-content-stats",
            async ct => await FetchStats(ct),
            _cacheOptions,
            cancellationToken: cancellationToken);
    }

    private async Task<PlatformContentStatsDto> FetchStats(CancellationToken cancellationToken)
    {
        // Один round-trip: подзапросы-счётчики PUBLISHED-контента. Quoted-алиасы
        // совпадают с конструктором record'а (без зависимости от MatchNamesWithUnderscores),
        // ::int нормализует bigint COUNT в Int32.
        const string sql = """
                           SELECT
                               (SELECT COUNT(*)::int FROM courses     WHERE status = 'PUBLISHED') AS "CoursesCount",
                               (SELECT COUNT(*)::int FROM collections WHERE status = 'PUBLISHED') AS "CollectionsCount",
                               (SELECT COUNT(*)::int FROM materials   WHERE status = 'PUBLISHED') AS "MaterialsCount",
                               (SELECT COUNT(*)::int FROM issues      WHERE status = 'PUBLISHED') AS "IssuesCount";
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();
        return await connection.QuerySingleAsync<PlatformContentStatsDto>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
    }
}
