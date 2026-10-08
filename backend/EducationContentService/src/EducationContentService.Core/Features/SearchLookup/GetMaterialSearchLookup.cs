using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.SearchLookup;
using EducationContentService.Core.Features.ContentAccess;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.SearchLookup;

public sealed record GetMaterialSearchLookupQuery(Guid MaterialId) : IQuery;

public sealed class GetMaterialSearchLookupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/search/materials/{materialId:guid}", async Task<EndpointResult<MaterialSearchLookupDto>> (
                [FromRoute] Guid materialId,
                [FromServices] GetMaterialSearchLookupHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetMaterialSearchLookupQuery(materialId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetMaterialSearchLookupHandler
    : IQueryHandlerWithResult<MaterialSearchLookupDto, GetMaterialSearchLookupQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetMaterialSearchLookupHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<MaterialSearchLookupDto, Error>> Handle(
        GetMaterialSearchLookupQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                m.id,
                m.author_id,
                m.title,
                m.image_id,
                m.video_id,
                m.kind,
                m.status,
                m.access_type,
                m.updated_at,
                SUBSTRING(m.content FROM 1 FOR 204800) AS content,
                m.chapter_titles,
                m.chapter_timestamps,
                parent.module_id,
                parent.module_title,
                parent.course_id,
                parent.course_slug,
                parent.course_title,
                COALESCE(parent.is_course_orphaned, false) AS is_course_orphaned
            FROM materials m
            -- Родительский курс материала + флаг course-orphan (issue #378). Та же логика,
            -- что в ExportSearchMaterials (полный реиндекс) — держать синхронно. rels собирает
            -- ВСЕ связи материала с курсами: course_materials (priority 0) и module_items→
            -- course_items (priority 1, defensive fallback при нарушении INV-4). Для отображения
            -- берём «лучший» курс — PUBLISHED раньше архивных, затем по priority.
            -- is_course_orphaned = есть ≥1 связь с курсом, но НИ один курс не PUBLISHED → search
            -- спрячет документ. Never-bound orphan (0 связей) → parent=NULL → COALESCE=false → виден (#77).
            LEFT JOIN LATERAL (
                WITH rels AS (
                    SELECT
                        0 AS priority,
                        NULL::uuid AS module_id,
                        NULL::text AS module_title,
                        cm.course_id,
                        c.slug AS course_slug,
                        c.title AS course_title,
                        (c.status = 'PUBLISHED') AS is_published
                    FROM course_materials cm
                    JOIN courses c
                        ON c.id = cm.course_id
                    WHERE cm.material_id = m.id

                    UNION ALL

                    SELECT
                        1 AS priority,
                        mi.module_id,
                        mo.title AS module_title,
                        ci.course_id,
                        c.slug AS course_slug,
                        c.title AS course_title,
                        (c.status = 'PUBLISHED') AS is_published
                    FROM module_items mi
                    JOIN modules mo
                        ON mo.id = mi.module_id
                    LEFT JOIN course_items ci
                        ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                    LEFT JOIN courses c
                        ON c.id = ci.course_id
                    WHERE mi.reference_id = m.id
                      AND mi.item_type = 'Material'
                )
                SELECT
                    best.module_id,
                    best.module_title,
                    best.course_id,
                    best.course_slug,
                    best.course_title,
                    (SELECT COUNT(*) FILTER (WHERE course_id IS NOT NULL) > 0
                            AND COUNT(*) FILTER (WHERE is_published) = 0
                     FROM rels) AS is_course_orphaned
                FROM (
                    SELECT *
                    FROM rels
                    ORDER BY is_published DESC NULLS LAST, priority
                    LIMIT 1
                ) best
            ) parent ON TRUE
            WHERE m.id = @MaterialId;
            """;

        MaterialSearchLookupRow? row = await connection.QueryFirstOrDefaultAsync<MaterialSearchLookupRow>(
            new CommandDefinition(sql, new { query.MaterialId }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return GeneralErrors.NotFound(query.MaterialId);
        }

        return new MaterialSearchLookupDto(
            row.Id,
            row.CourseId,
            row.CourseSlug,
            row.Title,
            row.ImageId,
            row.ModuleId,
            row.CourseTitle,
            row.ModuleTitle,
            SearchLookupEnumConverter.ToPublicationStatus(row.Status),
            ContentAccessTagBuilder.Build(
                row.AccessType,
                row.Id,
                row.CourseId.HasValue ? [row.CourseId.Value] : []),
            row.UpdatedAt,
            row.AuthorId,
            row.Kind,
            row.Content,
            row.VideoId,
            row.ChapterTitles ?? Array.Empty<string>(),
            row.ChapterTimestamps ?? Array.Empty<int>(),
            row.IsCourseOrphaned);
    }

    private sealed class MaterialSearchLookupRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public Guid? CourseId { get; init; }
        public string? CourseSlug { get; init; }
        public string Title { get; init; } = null!;
        public Guid? ImageId { get; init; }
        public Guid? ModuleId { get; init; }
        public string? CourseTitle { get; init; }
        public string? ModuleTitle { get; init; }
        public string Status { get; init; } = null!;
        public string AccessType { get; init; } = null!;
        public DateTime UpdatedAt { get; init; }
        public string Kind { get; init; } = null!;
        public string? Content { get; init; }
        public Guid? VideoId { get; init; }
        public string[]? ChapterTitles { get; init; }
        public int[]? ChapterTimestamps { get; init; }
        public bool IsCourseOrphaned { get; init; }
    }
}
