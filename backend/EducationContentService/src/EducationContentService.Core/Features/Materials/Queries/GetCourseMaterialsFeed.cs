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

public sealed record GetCourseMaterialsFeedQuery(
    Guid CourseId,
    string? Cursor,
    int Limit,
    string? Kind,
    IReadOnlyList<Guid>? TagIds,
    string? Search,
    string? AccessFilter) : IQuery;

public sealed class GetCourseMaterialsFeedEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/{courseId:guid}/materials/feed",
                async Task<EndpointResult<CursorResponse<MaterialFeedItemDto>>> (
                    [FromRoute] Guid courseId,
                    [FromQuery] string? cursor,
                    [FromQuery] int? limit,
                    [FromQuery] string? kind,
                    [FromQuery(Name = "tagIds")] Guid[]? tagIds,
                    [FromQuery] string? search,
                    [FromQuery] string? accessFilter,
                    [FromServices] GetCourseMaterialsFeedHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetCourseMaterialsFeedQuery(
                        courseId,
                        cursor,
                        limit is null or 0 ? 15 : limit.Value,
                        kind,
                        tagIds is { Length: > 0 } ? tagIds : null,
                        string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                        accessFilter),
                    cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetCourseMaterialsFeedHandler
    : IQueryHandler<CursorResponse<MaterialFeedItemDto>, GetCourseMaterialsFeedQuery>
{
    private const int MAX_LIMIT = 30;
    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IProgressServiceClient _progressServiceClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _userData;

    public GetCourseMaterialsFeedHandler(
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
        GetCourseMaterialsFeedQuery query,
        CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(query.Limit, 1, MAX_LIMIT);
        Cursor? cursor = Cursor.Decode(query.Cursor);

        DbConnection connection = _transactionManager.GetDbConnection();

        // `accessFilter=free` режет выдачу до AccessType ∈ {PUBLIC, REGISTERED, FREE}
        // (т. е. всё, кроме ENROLLED). null = без фильтра, обычная выдача.
        string[]? allowedAccessTypes = AccessFilterTypes.Resolve(query.AccessFilter);

        var parameters = new
        {
            query.CourseId,
            CursorPublishedAt = cursor?.CreatedAt,
            CursorId = cursor?.LastId,
            Limit = limit + 1,
            KindFilter = string.IsNullOrWhiteSpace(query.Kind) ? null : query.Kind,
            TagIds = query.TagIds?.ToArray(),
            SearchTerm = query.Search,
            AllowedAccessTypes = allowedAccessTypes,
        };

        // После унификации (MATERIAL_LIFECYCLE.md INV-4): материалы курса читаются из одного источника —
        // course_materials. Модули — упорядоченная ссылка на материал, который УЖЕ в course_materials.
        // moduleTitle берём subquery'ем (первый модуль курса, ссылающийся на этот материал).
        const string sql = """
                            SELECT
                                m.id,
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
                                (
                                    SELECT mod.title
                                    FROM modules mod
                                    JOIN module_items mi ON mi.module_id = mod.id
                                    JOIN course_items ci ON ci.reference_id = mod.id AND ci.item_type = 'Module'
                                    WHERE mi.item_type = 'Material'
                                      AND mi.reference_id = m.id
                                      AND ci.course_id = @CourseId
                                    ORDER BY ci.sort_key, mi.sort_key
                                    LIMIT 1
                                ) AS module_title,
                                c.id AS course_id,
                                c.title AS course_title,
                                c.slug AS course_slug
                            FROM course_materials cm
                            JOIN materials m ON m.id = cm.material_id
                            JOIN courses c ON c.id = cm.course_id AND c.status = 'PUBLISHED'
                            WHERE cm.course_id = @CourseId
                              AND m.status = 'PUBLISHED'
                              AND m.published_at IS NOT NULL
                              AND (@KindFilter IS NULL OR m.kind = @KindFilter)
                              AND (@TagIds::uuid[] IS NULL OR EXISTS (
                                SELECT 1 FROM tags.entity_tags et
                                WHERE et.entity_type = 'Material'
                                  AND et.entity_id = m.id
                                  AND et.tag_id = ANY(@TagIds)
                              ))
                              AND (@SearchTerm IS NULL OR m.title ILIKE '%' || @SearchTerm || '%')
                              AND (@AllowedAccessTypes::text[] IS NULL OR m.access_type = ANY(@AllowedAccessTypes))
                              AND (@CursorPublishedAt IS NULL OR (m.published_at, m.id) < (@CursorPublishedAt, @CursorId))
                            ORDER BY m.published_at DESC, m.id DESC
                            LIMIT @Limit;
                            """;

        List<MaterialFeedItemDto> materials = (await connection.QueryAsync<MaterialFeedItemDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).ToList();

        bool hasMore = materials.Count > limit;
        if (hasMore)
            materials.RemoveAt(materials.Count - 1);

        if (materials.Count == 0)
            return new CursorResponse<MaterialFeedItemDto>
            {
                Items = [],
                NextCursor = null,
                TotalCount = 0
            };

        materials = await MaterialFeedEnricher.EnrichAsync(
            materials, _fileServiceClient, _entitlementChecker, _progressServiceClient, _userData, cancellationToken);

        MaterialFeedItemDto? lastItem = hasMore ? materials[^1] : null;
        string? nextCursor = lastItem?.PublishedAt is { } lastPublishedAt
            ? Cursor.Encode(new DateTimeOffset(lastPublishedAt, TimeSpan.Zero), lastItem.Id)
            : null;

        // TotalCount=0 — sentinel: infinite-scroll фид не считает total (COUNT(*) дорогой
        // с фильтрами access/kind). Фронт использует hasNextPage из cursor, не TotalCount.
        return new CursorResponse<MaterialFeedItemDto>
        {
            Items = materials,
            NextCursor = nextCursor,
            TotalCount = 0
        };
    }
}
