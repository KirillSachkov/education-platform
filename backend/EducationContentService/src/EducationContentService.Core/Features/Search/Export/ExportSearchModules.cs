using System.Data.Common;
using Core.Abstractions;
using EducationContentService.Core.Constant;
using Core.Database;
using Core.Validation;
using Dapper;
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

public sealed record ExportSearchModulesQuery(ExportSearchModulesRequest Request) : IQuery;
public sealed class ExportSearchModulesQueryValidator : AbstractValidator<ExportSearchModulesQuery>
{
    public ExportSearchModulesQueryValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(1, Constants.MAX_SEARCH_EXPORT_LIMIT)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchModulesRequest.Limit)));
        RuleFor(x => x.Request.Cursor)
            .Must(cursor => string.IsNullOrWhiteSpace(cursor) || Cursor.Decode(cursor) is not null)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchModulesRequest.Cursor)));
    }
}

public sealed class ExportSearchModulesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/search/export/entities/module", async Task<EndpointResult<CursorResponse<SearchExportEntityDto>>> (
                [AsParameters] ExportSearchModulesRequest request,
                [FromServices] ExportSearchModulesHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new ExportSearchModulesQuery(request), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class ExportSearchModulesHandler
    : IQueryHandlerWithResult<CursorResponse<SearchExportEntityDto>, ExportSearchModulesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<ExportSearchModulesQuery> _validator;

    public ExportSearchModulesHandler(
        ITransactionManager transactionManager,
        IValidator<ExportSearchModulesQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<CursorResponse<SearchExportEntityDto>, Error>> Handle(
        ExportSearchModulesQuery query,
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
                1 AS entity_type,
                m.id AS entity_id,
                m.title,
                m.description,
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
                m.updated_at,
                m.author_id
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
              AND (@CursorUpdatedAt::timestamptz IS NULL
                   OR (m.updated_at, m.id) > (@CursorUpdatedAt::timestamptz, @CursorEntityId::uuid))
            GROUP BY
                m.id,
                m.title,
                m.description,
                c.id,
                c.slug,
                c.title,
                m.updated_at,
                m.author_id
            ORDER BY m.updated_at, m.id
            LIMIT @LimitPlusOne;

            SELECT COUNT(*)::bigint
            FROM modules m
            WHERE m.status = 'PUBLISHED';
            """;

        using SqlMapper.GridReader result = await connection.QueryMultipleAsync(
            new CommandDefinition(
                sql,
                new
                {
                    CursorUpdatedAt = cursor?.CreatedAt.UtcDateTime,
                    CursorEntityId = cursor?.LastId,
                    LimitPlusOne = query.Request.Limit + 1,
                },
                cancellationToken: cancellationToken));

        SearchExportEntityRow[] rows = (await result.ReadAsync<SearchExportEntityRow>()).ToArray();
        long totalCount = await result.ReadSingleAsync<long>();

        SearchExportEntityDto[] items = rows
            .Take(query.Request.Limit)
            .Select(static row => new SearchExportEntityDto
            {
                EntityType = row.EntityType,
                EntityId = row.EntityId,
                Title = row.Title,
                Description = row.Description,
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
