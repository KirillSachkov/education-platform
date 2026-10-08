using System.Data.Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts;
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

public sealed record GetAuthorMaterialsFeedQuery(
    Guid AuthorId,
    string? Cursor,
    int Limit,
    string? Kind,
    string Scope,
    string? Search,
    IReadOnlyList<Guid>? TagIds,
    string? AccessFilter) : IQuery;

public sealed class GetAuthorMaterialsFeedEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("authors/{authorId:guid}/materials/feed",
                async Task<EndpointResult<CursorResponse<MaterialFeedItemDto>>> (
                    [FromRoute] Guid authorId,
                    [FromQuery] string? cursor,
                    [FromQuery] int? limit,
                    [FromQuery] string? kind,
                    [FromQuery] string? scope,
                    [FromQuery] string? search,
                    [FromQuery(Name = "tagIds")] Guid[]? tagIds,
                    [FromQuery] string? accessFilter,
                    [FromServices] GetAuthorMaterialsFeedHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetAuthorMaterialsFeedQuery(
                        authorId,
                        cursor,
                        limit is null or 0 ? 15 : limit.Value,
                        kind,
                        string.IsNullOrWhiteSpace(scope) ? "all" : scope.ToLowerInvariant(),
                        search,
                        tagIds is { Length: > 0 } ? tagIds : null,
                        accessFilter),
                    cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetAuthorMaterialsFeedHandler
    : IQueryHandler<CursorResponse<MaterialFeedItemDto>, GetAuthorMaterialsFeedQuery>
{
    private const int MAX_LIMIT = 30;
    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IProgressServiceClient _progressServiceClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _userData;

    public GetAuthorMaterialsFeedHandler(
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

    public async Task<CursorResponse<MaterialFeedItemDto>> Handle(
        GetAuthorMaterialsFeedQuery query,
        CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(query.Limit, 1, MAX_LIMIT);
        Cursor? cursor = Cursor.Decode(query.Cursor);
        bool isEnrolledScope = string.Equals(query.Scope, "enrolled", StringComparison.Ordinal);

        // For scope=enrolled: resolve user's enrolled course ids (including trial) from Redis.
        Guid[]? enrolledCourseIds = null;
        if (isEnrolledScope)
        {
            if (!_userData.IsAuthenticated)
                return EmptyResponse();

            IReadOnlySet<Guid> ids = await _entitlementChecker.GetUserEnrolledCourseIdsAsync(
                _userData.UserId, includeTrial: true, cancellationToken);

            if (ids.Count == 0)
                return EmptyResponse();

            enrolledCourseIds = ids.ToArray();
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        string? searchPattern = string.IsNullOrWhiteSpace(query.Search)
            ? null
            : $"%{query.Search.Trim().ToLowerInvariant()}%";

        string[]? allowedAccessTypes = AccessFilterTypes.Resolve(query.AccessFilter);

        var parameters = new
        {
            query.AuthorId,
            CursorPublishedAt = cursor?.CreatedAt,
            CursorId = cursor?.LastId,
            Limit = limit + 1,
            KindFilter = string.IsNullOrWhiteSpace(query.Kind) ? null : query.Kind,
            EnrolledCourseIds = enrolledCourseIds,
            SearchPattern = searchPattern,
            TagIds = query.TagIds?.ToArray(),
            AllowedAccessTypes = allowedAccessTypes,
        };

        // После унификации (MATERIAL_LIFECYCLE.md INV-4): привязка материала к курсу живёт
        // ровно в course_materials. module_items для модулей курса всегда сопровождаются
        // course_materials (обеспечено use-case AttachMaterialToModule + миграцией backfill).
        //
        // scope=all → LEFT JOIN course_materials (включая space-level материалы без привязок).
        // scope=enrolled → INNER JOIN course_materials с фильтром по enrolled courseIds.
        //
        // Материал, привязанный к нескольким курсам, появится один раз (DISTINCT ON по material.id).
        // В pick'е поля courseId/courseTitle выбирается первый course по sort_key course_materials.
        // SQL-варианты как const-строки. isEnrolledScope — серверный boolean; выбор между
        // двумя заведомо безопасными строками, а не интерполяция. Сняты false-positive'ы SAST.
        const string scopeAllSql = """
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
                                           cm.sort_key AS cm_sort_key,
                                           ROW_NUMBER() OVER (PARTITION BY m.id ORDER BY cm.sort_key NULLS LAST) AS rn
                                       FROM materials m
                                       LEFT JOIN course_materials cm ON cm.material_id = m.id
                                       WHERE m.status = 'PUBLISHED'
                                         AND m.author_id = @AuthorId
                                         AND m.published_at IS NOT NULL
                                         AND (@KindFilter IS NULL OR m.kind = @KindFilter)
                                         AND (@SearchPattern IS NULL OR lower(m.title) LIKE @SearchPattern)
                                         AND (@TagIds::uuid[] IS NULL OR EXISTS (
                                           SELECT 1 FROM tags.entity_tags et
                                           WHERE et.entity_type = 'Material'
                                             AND et.entity_id = m.id
                                             AND et.tag_id = ANY(@TagIds)
                                         ))
                                         AND (@AllowedAccessTypes::text[] IS NULL OR m.access_type = ANY(@AllowedAccessTypes))
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
                                     AND (@CursorPublishedAt IS NULL OR (r.published_at, r.material_id) < (@CursorPublishedAt, @CursorId))
                                   ORDER BY r.published_at DESC, r.material_id DESC
                                   LIMIT @Limit;
                                   """;

        const string scopeEnrolledSql = """
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
                                                cm.sort_key AS cm_sort_key,
                                                ROW_NUMBER() OVER (PARTITION BY m.id ORDER BY cm.sort_key NULLS LAST) AS rn
                                            FROM materials m
                                            JOIN course_materials cm ON cm.material_id = m.id AND cm.course_id = ANY(@EnrolledCourseIds)
                                            WHERE m.status = 'PUBLISHED'
                                              AND m.author_id = @AuthorId
                                              AND m.published_at IS NOT NULL
                                              AND (@KindFilter IS NULL OR m.kind = @KindFilter)
                                         AND (@SearchPattern IS NULL OR lower(m.title) LIKE @SearchPattern)
                                         AND (@TagIds::uuid[] IS NULL OR EXISTS (
                                           SELECT 1 FROM tags.entity_tags et
                                           WHERE et.entity_type = 'Material'
                                             AND et.entity_id = m.id
                                             AND et.tag_id = ANY(@TagIds)
                                         ))
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
                                          AND (@CursorPublishedAt IS NULL OR (r.published_at, r.material_id) < (@CursorPublishedAt, @CursorId))
                                        ORDER BY r.published_at DESC, r.material_id DESC
                                        LIMIT @Limit;
                                        """;

        string sql = isEnrolledScope ? scopeEnrolledSql : scopeAllSql;

        List<MaterialFeedItemDto> materials = (await connection.QueryAsync<MaterialFeedItemDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).ToList();

        bool hasMore = materials.Count > limit;
        if (hasMore)
            materials.RemoveAt(materials.Count - 1);

        if (materials.Count == 0)
            return EmptyResponse();

        materials = await MaterialFeedEnricher.EnrichAsync(
            materials, _fileServiceClient, _entitlementChecker, _progressServiceClient, _userData, cancellationToken);

        MaterialFeedItemDto? lastItem = hasMore ? materials[^1] : null;
        string? nextCursor = lastItem?.PublishedAt is { } lastPublishedAt
            ? Cursor.Encode(new DateTimeOffset(lastPublishedAt, TimeSpan.Zero), lastItem.Id)
            : null;

        // TotalCount=0 — это sentinel: infinite-scroll фид не считает общее число материалов,
        // COUNT(*) по тем же фильтрам (DISTINCT ON + LEFT JOIN) дорогой. Фронт использует
        // hasNextPage и courseId из cursor, не TotalCount.
        return new CursorResponse<MaterialFeedItemDto>
        {
            Items = materials,
            NextCursor = nextCursor,
            TotalCount = 0
        };
    }

    private static CursorResponse<MaterialFeedItemDto> EmptyResponse() => new()
    {
        Items = [],
        NextCursor = null,
        TotalCount = 0 // см. комментарий выше — TotalCount не вычисляется для feed.
    };
}
