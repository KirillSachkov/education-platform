using System.Data.Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Materials;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using ProgressService.Contracts.HttpCommunication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Materials.Queries;

/// <summary>
/// Лёгкий endpoint для виджета «Бесплатно от автора» на лендинге.
/// Возвращает до 12 свежих опубликованных материалов автора с
/// <c>AccessType ∈ {PUBLIC, REGISTERED, FREE}</c>. ENROLLED-материалы скрыты.
/// </summary>
public sealed record GetAuthorFreeMaterialsQuery(Guid AuthorId, int Limit) : IQuery;

/// <summary>
/// Wrapper над списком — нужен для <c>EndpointResult&lt;T&gt;</c>: implicit-конверсия
/// у фреймворка работает для конкретных типов, а не для интерфейсов (<c>IReadOnlyList</c>).
/// </summary>
public sealed record AuthorFreeMaterialsDto(IReadOnlyList<MaterialFeedItemDto> Items);

public sealed class GetAuthorFreeMaterialsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(
                "authors/{authorId:guid}/free-materials",
                async Task<EndpointResult<AuthorFreeMaterialsDto>> (
                    [FromRoute] Guid authorId,
                    [FromQuery] int? limit,
                    [FromServices] GetAuthorFreeMaterialsHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new GetAuthorFreeMaterialsQuery(
                            authorId,
                            Math.Clamp(limit ?? 6, 1, MaxLimit)),
                        cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }

    private const int MaxLimit = 24;
}

public sealed class GetAuthorFreeMaterialsHandler
    : IQueryHandler<AuthorFreeMaterialsDto, GetAuthorFreeMaterialsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IProgressServiceClient _progressServiceClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _userData;

    public GetAuthorFreeMaterialsHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        IProgressServiceClient progressServiceClient,
        IEntitlementChecker entitlementChecker,
        UserScopedData userData)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _progressServiceClient = progressServiceClient;
        _entitlementChecker = entitlementChecker;
        _userData = userData;
    }

    public async Task<AuthorFreeMaterialsDto> Handle(
        GetAuthorFreeMaterialsQuery query,
        CancellationToken cancellationToken = default)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        var parameters = new
        {
            query.AuthorId,
            query.Limit,
            AllowedAccessTypes = AccessFilterTypes.Free,
        };

        // Подобно GetAuthorMaterialsFeed.scopeAllSql, но без курсора, kind/tag/search-фильтров,
        // с жёстким `access_type = ANY(...)` под виджет. ROW_NUMBER() дедуплицирует
        // материалы, привязанные к нескольким курсам (берём «первый» course для контекста).
        const string sql = """
                           WITH ranked AS (
                               SELECT
                                   m.id AS material_id,
                                   m.author_id,
                                   m.title,
                                   LEFT(m.content, 280) AS preview,
                                   m.kind,
                                   m.status,
                                   m.access_type,
                                   m.created_at,
                                   m.updated_at,
                                   m.published_at,
                                   m.image_id,
                                   m.video_id,
                                   cm.course_id,
                                   ROW_NUMBER() OVER (PARTITION BY m.id ORDER BY cm.sort_key NULLS LAST) AS rn
                               FROM materials m
                               LEFT JOIN course_materials cm ON cm.material_id = m.id
                               WHERE m.status = 'PUBLISHED'
                                 AND m.author_id = @AuthorId
                                 AND m.published_at IS NOT NULL
                                 AND m.access_type = ANY(@AllowedAccessTypes)
                           )
                           SELECT
                               r.material_id AS id,
                               r.author_id,
                               r.title,
                               r.preview,
                               r.kind,
                               r.status,
                               r.access_type,
                               r.created_at,
                               r.updated_at,
                               r.published_at,
                               r.image_id,
                               r.video_id,
                               (
                                   SELECT mod.title
                                   FROM modules mod
                                   JOIN module_items mi ON mi.module_id = mod.id
                                   JOIN course_items ci ON ci.reference_id = mod.id AND ci.item_type = 'Module'
                                   WHERE mi.item_type = 'Material'
                                     AND mi.reference_id = r.material_id
                                     AND ci.course_id = r.course_id
                                   ORDER BY ci.sort_key, mi.sort_key
                                   LIMIT 1
                               ) AS module_title,
                               c.id AS course_id,
                               c.title AS course_title,
                               c.slug AS course_slug
                           FROM ranked r
                           LEFT JOIN courses c ON c.id = r.course_id AND c.status = 'PUBLISHED'
                           WHERE r.rn = 1
                           ORDER BY r.published_at DESC, r.material_id DESC
                           LIMIT @Limit;
                           """;

        List<MaterialFeedItemDto> materials = (await connection.QueryAsync<MaterialFeedItemDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).ToList();

        if (materials.Count == 0)
        {
            return new AuthorFreeMaterialsDto([]);
        }

        List<MaterialFeedItemDto> enriched = await MaterialFeedEnricher.EnrichAsync(
            materials,
            _fileServiceClient,
            _entitlementChecker,
            _progressServiceClient,
            _userData,
            cancellationToken);

        return new AuthorFreeMaterialsDto(enriched);
    }
}
