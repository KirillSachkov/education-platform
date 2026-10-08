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

public sealed record ExportSearchMaterialsQuery(ExportSearchMaterialsRequest Request) : IQuery;
public sealed class ExportSearchMaterialsQueryValidator : AbstractValidator<ExportSearchMaterialsQuery>
{
    public ExportSearchMaterialsQueryValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(1, Constants.MAX_SEARCH_EXPORT_LIMIT)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchMaterialsRequest.Limit)));
        RuleFor(x => x.Request.Cursor)
            .Must(cursor => string.IsNullOrWhiteSpace(cursor) || Cursor.Decode(cursor) is not null)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchMaterialsRequest.Cursor)));
    }
}

public sealed class ExportSearchMaterialsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/search/export/entities/material", async Task<EndpointResult<CursorResponse<SearchExportEntityDto>>> (
                [AsParameters] ExportSearchMaterialsRequest request,
                [FromServices] ExportSearchMaterialsHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new ExportSearchMaterialsQuery(request), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class ExportSearchMaterialsHandler
    : IQueryHandlerWithResult<CursorResponse<SearchExportEntityDto>, ExportSearchMaterialsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<ExportSearchMaterialsQuery> _validator;

    public ExportSearchMaterialsHandler(
        ITransactionManager transactionManager,
        IValidator<ExportSearchMaterialsQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<CursorResponse<SearchExportEntityDto>, Error>> Handle(
        ExportSearchMaterialsQuery query,
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
                m.updated_at,
                m.author_id,
                m.kind AS material_kind,
                SUBSTRING(m.content FROM 1 FOR 204800) AS content,
                m.video_id,
                m.chapter_titles,
                m.chapter_timestamps,
                COALESCE(parent.is_course_orphaned, false) AS is_course_orphaned
            FROM materials m
            -- Родительский курс материала + флаг course-orphan (issue #378).
            -- rels собирает ВСЕ связи материала с курсами: course_materials (priority 0)
            -- и module_items→course_items (priority 1, defensive fallback при нарушении INV-4 —
            -- в нормальном потоке через use-cases не срабатывает, т.к. AttachMaterialToModule
            -- идемпотентно создаёт course_materials).
            -- Для отображения берём «лучший» курс — PUBLISHED раньше архивных, затем по priority.
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
            WHERE m.status = 'PUBLISHED'
              AND (@CursorUpdatedAt::timestamptz IS NULL
                   OR (m.updated_at, m.id) > (@CursorUpdatedAt::timestamptz, @CursorEntityId::uuid))
            ORDER BY m.updated_at, m.id
            LIMIT @LimitPlusOne;

            SELECT COUNT(*)::bigint
            FROM materials m
            WHERE m.status = 'PUBLISHED';
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
                UpdatedAt = row.UpdatedAt,
                AuthorId = row.AuthorId,
                MaterialKind = row.MaterialKind,
                Content = row.Content,
                VideoId = row.VideoId,
                ChapterTitles = row.ChapterTitles ?? Array.Empty<string>(),
                ChapterTimestamps = row.ChapterTimestamps ?? Array.Empty<int>(),
                IsCourseOrphaned = row.IsCourseOrphaned,
            })
            .ToArray();

        return new CursorResponse<SearchExportEntityDto>
        {
            Items = items,
            NextCursor = rows.Length > query.Request.Limit ? Cursor.Encode(items[^1].UpdatedAt, items[^1].EntityId) : null,
            TotalCount = totalCount,
        };
    }
}
