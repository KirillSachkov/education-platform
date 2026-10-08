using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Core.Constant;
using EducationContentService.Contracts;
using EducationContentService.Contracts.SearchExport;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Search.Export;

public sealed record ExportSearchEntitiesQuery(ExportSearchEntitiesRequest Request) : IQuery;
public sealed class ExportSearchEntitiesQueryValidator : AbstractValidator<ExportSearchEntitiesQuery>
{
    public ExportSearchEntitiesQueryValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(1, Constants.MAX_SEARCH_EXPORT_LIMIT)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchEntitiesRequest.Limit)));
        RuleFor(x => x.Request.Cursor)
            .Must(cursor => string.IsNullOrWhiteSpace(cursor) || Cursor.Decode(cursor) is not null)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchEntitiesRequest.Cursor)));
    }
}

public sealed class ExportSearchEntitiesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/search/export/entities", async Task<EndpointResult<CursorResponse<SearchExportEntityDto>>> (
                [AsParameters] ExportSearchEntitiesRequest request,
                [FromServices] ExportSearchEntitiesHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new ExportSearchEntitiesQuery(request), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class ExportSearchEntitiesHandler
    : IQueryHandlerWithResult<CursorResponse<SearchExportEntityDto>, ExportSearchEntitiesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<ExportSearchEntitiesQuery> _validator;

    public ExportSearchEntitiesHandler(
        ITransactionManager transactionManager,
        IValidator<ExportSearchEntitiesQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<CursorResponse<SearchExportEntityDto>, Error>> Handle(
        ExportSearchEntitiesQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Cursor? cursor = Cursor.Decode(query.Request.Cursor);

        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            WITH entities AS (
                SELECT
                    0 AS entity_type,
                    c.id AS entity_id,
                    c.title,
                    c.description,
                    NULL::uuid AS image_id,
                    ARRAY['PUBLIC']::text[] AS access_types,
                    c.id AS course_id,
                    c.slug AS course_slug,
                    c.title AS course_title,
                    NULL::uuid AS project_id,
                    NULL::text AS project_title,
                    NULL::uuid AS module_id,
                    NULL::text AS module_title,
                    NULL::text AS material_kind,
                    NULL::text AS content,
                    NULL::uuid AS video_id,
                    c.author_id,
                    c.updated_at
                FROM courses c
                WHERE c.status = 'PUBLISHED'

                UNION ALL

                SELECT
                    1 AS entity_type,
                    m.id AS entity_id,
                    m.title,
                    m.description,
                    NULL::uuid AS image_id,
                    CASE
                        WHEN COUNT(DISTINCT item_access.access_type) FILTER (WHERE item_access.access_type IS NOT NULL) > 0
                            THEN ARRAY_AGG(DISTINCT item_access.access_type) FILTER (WHERE item_access.access_type IS NOT NULL)
                        ELSE ARRAY['PUBLIC']::text[]
                    END AS access_types,
                    c.id AS course_id,
                    c.slug AS course_slug,
                    c.title AS course_title,
                    NULL::uuid AS project_id,
                    NULL::text AS project_title,
                    m.id AS module_id,
                    m.title AS module_title,
                    NULL::text AS material_kind,
                    NULL::text AS content,
                    NULL::uuid AS video_id,
                    m.author_id,
                    m.updated_at
                FROM modules m
                LEFT JOIN course_items ci
                    ON ci.reference_id = m.id AND ci.item_type = 'Module'
                LEFT JOIN courses c
                    ON c.id = ci.course_id
                LEFT JOIN LATERAL (
                    SELECT
                        mat.access_type AS access_type
                    FROM module_items mi
                    JOIN materials mat
                        ON mi.item_type = 'Material'
                       AND mi.reference_id = mat.id
                       AND mat.status = 'PUBLISHED'
                    WHERE mi.module_id = m.id

                    UNION ALL

                    SELECT
                        iss.access_type AS access_type
                    FROM module_items mi
                    JOIN issues iss
                        ON mi.item_type = 'Issue'
                       AND mi.reference_id = iss.id
                       AND iss.status = 'PUBLISHED'
                    WHERE mi.module_id = m.id
                ) item_access ON TRUE
                WHERE m.status = 'PUBLISHED'
                GROUP BY
                    m.id,
                    m.title,
                    m.description,
                    c.id,
                    c.title,
                    m.author_id,
                    m.updated_at

                UNION ALL

                SELECT
                    2 AS entity_type,
                    p.id AS entity_id,
                    p.title,
                    p.description,
                    NULL::uuid AS image_id,
                    CASE
                        WHEN COUNT(DISTINCT issue_access.access_type) FILTER (WHERE issue_access.access_type IS NOT NULL) > 0
                            THEN ARRAY_AGG(DISTINCT issue_access.access_type) FILTER (WHERE issue_access.access_type IS NOT NULL)
                        ELSE ARRAY['PUBLIC']::text[]
                    END AS access_types,
                    c.id AS course_id,
                    c.slug AS course_slug,
                    c.title AS course_title,
                    p.id AS project_id,
                    p.title AS project_title,
                    NULL::uuid AS module_id,
                    NULL::text AS module_title,
                    NULL::text AS material_kind,
                    NULL::text AS content,
                    NULL::uuid AS video_id,
                    p.author_id,
                    p.updated_at
                FROM projects p
                LEFT JOIN course_items ci
                    ON ci.reference_id = p.id AND ci.item_type = 'Project'
                LEFT JOIN courses c
                    ON c.id = ci.course_id
                LEFT JOIN project_items pi
                    ON pi.project_id = p.id
                LEFT JOIN issues i
                    ON i.id = pi.issue_id
                   AND i.status = 'PUBLISHED'
                LEFT JOIN LATERAL (
                    SELECT
                        i.access_type AS access_type
                ) issue_access ON TRUE
                WHERE p.status = 'PUBLISHED'
                GROUP BY
                    p.id,
                    p.title,
                    p.description,
                    c.id,
                    c.title,
                    p.author_id,
                    p.updated_at

                UNION ALL

                SELECT
                    5 AS entity_type,
                    m.id AS entity_id,
                    m.title,
                    NULL::text AS description,
                    m.image_id,
                    ARRAY[m.access_type]::text[] AS access_types,
                    parent.course_id,
                    parent.course_slug,
                    parent.course_title,
                    NULL::uuid AS project_id,
                    NULL::text AS project_title,
                    parent.module_id,
                    parent.module_title,
                    m.kind AS material_kind,
                    SUBSTRING(m.content FROM 1 FOR 204800) AS content,
                    m.video_id,
                    m.author_id,
                    m.updated_at
                FROM materials m
                LEFT JOIN LATERAL (
                    SELECT
                        rel.module_id,
                        rel.module_title,
                        rel.course_id,
                        rel.course_slug,
                        rel.course_title
                    FROM (
                        SELECT
                            0 AS priority,
                            NULL::uuid AS module_id,
                            NULL::text AS module_title,
                            cm.course_id,
                            c.slug AS course_slug,
                            c.title AS course_title
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
                            c.title AS course_title
                        FROM module_items mi
                        JOIN modules mo
                            ON mo.id = mi.module_id
                        LEFT JOIN course_items ci
                            ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                        LEFT JOIN courses c
                            ON c.id = ci.course_id
                        WHERE mi.reference_id = m.id
                          AND mi.item_type = 'Material'
                    ) rel
                    ORDER BY rel.priority
                    LIMIT 1
                ) parent ON TRUE
                WHERE m.status = 'PUBLISHED'

                UNION ALL

                SELECT
                    3 AS entity_type,
                    i.id AS entity_id,
                    i.title,
                    NULL::text AS description,
                    NULL::uuid AS image_id,
                    ARRAY[i.access_type]::text[] AS access_types,
                    COALESCE(project_parent.course_id, module_parent.course_id) AS course_id,
                    COALESCE(project_parent.course_slug, module_parent.course_slug) AS course_slug,
                    COALESCE(project_parent.course_title, module_parent.course_title) AS course_title,
                    i.project_id,
                    p.title AS project_title,
                    module_parent.module_id,
                    module_parent.module_title,
                    NULL::text AS material_kind,
                    NULL::text AS content,
                    NULL::uuid AS video_id,
                    i.author_id,
                    i.updated_at
                FROM issues i
                LEFT JOIN projects p
                    ON p.id = i.project_id
                LEFT JOIN LATERAL (
                    SELECT
                        ci.course_id,
                        c.slug AS course_slug,
                        c.title AS course_title
                    FROM course_items ci
                    JOIN courses c
                        ON c.id = ci.course_id
                    WHERE ci.reference_id = i.project_id
                      AND ci.item_type = 'Project'
                    LIMIT 1
                ) project_parent ON TRUE
                LEFT JOIN LATERAL (
                    SELECT
                        mi.module_id,
                        mo.title AS module_title,
                        ci.course_id,
                        c.slug AS course_slug,
                        c.title AS course_title
                    FROM module_items mi
                    JOIN modules mo
                        ON mo.id = mi.module_id
                    LEFT JOIN course_items ci
                        ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                    LEFT JOIN courses c
                        ON c.id = ci.course_id
                    WHERE mi.reference_id = i.id
                      AND mi.item_type = 'Issue'
                    LIMIT 1
                ) module_parent ON TRUE
                WHERE i.status = 'PUBLISHED'

                UNION ALL

                -- Collections (entity_type=6). Access type обязан совпадать с
                -- GetCollectionSearchLookup / lifecycle handlers; иначе full reindex
                -- сбрасывает ENROLLED/REGISTERED подборки в PUBLIC.
                SELECT
                    6 AS entity_type,
                    coll.id AS entity_id,
                    coll.title,
                    coll.description,
                    coll.cover_image_id AS image_id,
                    ARRAY[coll.access_type]::text[] AS access_types,
                    coll.course_id,
                    c.slug AS course_slug,
                    c.title AS course_title,
                    NULL::uuid AS project_id,
                    NULL::text AS project_title,
                    NULL::uuid AS module_id,
                    NULL::text AS module_title,
                    NULL::text AS material_kind,
                    NULL::text AS content,
                    NULL::uuid AS video_id,
                    coll.author_id,
                    coll.updated_at
                FROM collections coll
                LEFT JOIN courses c
                    ON c.id = coll.course_id AND c.status = 'PUBLISHED'
                WHERE coll.status = 'PUBLISHED'
            )
            SELECT
                entity_type,
                entity_id,
                title,
                description,
                image_id,
                access_types,
                course_id,
                course_slug,
                course_title,
                project_id,
                project_title,
                module_id,
                module_title,
                material_kind,
                content,
                video_id,
                author_id,
                updated_at,
                -- is_course_orphaned (issue #378): только для материалов (entity_type=5).
                -- True когда у материала есть ≥1 связь с курсом, но НИ один из этих курсов
                -- не PUBLISHED (все архивированы/draft) → search прячет документ. Never-bound
                -- orphan (0 связей с реальным курсом) → false → остаётся видимым (#77).
                -- Должно совпадать с GetMaterialSearchLookup / ExportSearchMaterials, иначе
                -- nightly full-reindex вернёт спрятанный материал обратно в выдачу.
                CASE WHEN entity_type = 5 THEN (
                    SELECT COUNT(*) FILTER (WHERE rel_c.id IS NOT NULL) > 0
                           AND COUNT(*) FILTER (WHERE rel_c.status = 'PUBLISHED') = 0
                    FROM (
                        SELECT cm.course_id AS cid
                        FROM course_materials cm
                        WHERE cm.material_id = entities.entity_id

                        UNION ALL

                        SELECT ci.course_id AS cid
                        FROM module_items mi
                        LEFT JOIN course_items ci
                            ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                        WHERE mi.reference_id = entities.entity_id
                          AND mi.item_type = 'Material'
                    ) rels
                    LEFT JOIN courses rel_c ON rel_c.id = rels.cid
                ) ELSE false END AS is_course_orphaned
            FROM entities
            WHERE @CursorUpdatedAt::timestamptz IS NULL
               OR (updated_at, entity_id) > (@CursorUpdatedAt::timestamptz, @CursorEntityId::uuid)
            ORDER BY updated_at, entity_id
            LIMIT @LimitPlusOne;

            SELECT (
                (SELECT COUNT(*) FROM courses c WHERE c.status = 'PUBLISHED') +
                (SELECT COUNT(*) FROM modules m WHERE m.status = 'PUBLISHED') +
                (SELECT COUNT(*) FROM projects p WHERE p.status = 'PUBLISHED') +
                (SELECT COUNT(*) FROM materials m WHERE m.status = 'PUBLISHED') +
                (SELECT COUNT(*) FROM issues i WHERE i.status = 'PUBLISHED') +
                (SELECT COUNT(*) FROM collections coll WHERE coll.status = 'PUBLISHED')
            )::bigint;
            """;

        using var reader = await connection.QueryMultipleAsync(
            new CommandDefinition(
                sql,
                new
                {
                    CursorUpdatedAt = cursor?.CreatedAt.UtcDateTime,
                    CursorEntityId = cursor?.LastId,
                    LimitPlusOne = query.Request.Limit + 1,
                },
                cancellationToken: cancellationToken));

        SearchExportEntityRow[] rows = (await reader.ReadAsync<SearchExportEntityRow>()).ToArray();
        long totalCount = await reader.ReadSingleAsync<long>();

        SearchExportEntityDto[] items = rows
            .Take(query.Request.Limit)
            .Select(row => new SearchExportEntityDto
            {
                EntityType = row.EntityType,
                EntityId = row.EntityId,
                Title = row.Title,
                Description = row.Description,
                ImageId = row.ImageId,
                RequiredAccessTags = row.BuildRequiredAccessTags(),
                CourseId = row.CourseId,
                CourseSlug = row.CourseSlug,
                CourseTitle = row.CourseTitle,
                ProjectId = row.ProjectId,
                ProjectTitle = row.ProjectTitle,
                ModuleId = row.ModuleId,
                ModuleTitle = row.ModuleTitle,
                MaterialKind = row.MaterialKind,
                Content = row.Content,
                VideoId = row.VideoId,
                AuthorId = row.AuthorId,
                UpdatedAt = row.UpdatedAt,
                IsCourseOrphaned = row.IsCourseOrphaned,
            })
            .ToArray();

        return new CursorResponse<SearchExportEntityDto>
        {
            Items = items,
            NextCursor = rows.Length > query.Request.Limit
                ? Cursor.Encode(items[^1].UpdatedAt, items[^1].EntityId)
                : null,
            TotalCount = totalCount,
        };
    }
}
