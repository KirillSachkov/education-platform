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

public sealed record ExportSearchIssuesQuery(ExportSearchIssuesRequest Request) : IQuery;
public sealed class ExportSearchIssuesQueryValidator : AbstractValidator<ExportSearchIssuesQuery>
{
    public ExportSearchIssuesQueryValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(1, Constants.MAX_SEARCH_EXPORT_LIMIT)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchIssuesRequest.Limit)));
        RuleFor(x => x.Request.Cursor)
            .Must(cursor => string.IsNullOrWhiteSpace(cursor) || Cursor.Decode(cursor) is not null)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchIssuesRequest.Cursor)));
    }
}

public sealed class ExportSearchIssuesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/search/export/entities/issue", async Task<EndpointResult<CursorResponse<SearchExportEntityDto>>> (
                [AsParameters] ExportSearchIssuesRequest request,
                [FromServices] ExportSearchIssuesHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new ExportSearchIssuesQuery(request), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class ExportSearchIssuesHandler
    : IQueryHandlerWithResult<CursorResponse<SearchExportEntityDto>, ExportSearchIssuesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<ExportSearchIssuesQuery> _validator;

    public ExportSearchIssuesHandler(
        ITransactionManager transactionManager,
        IValidator<ExportSearchIssuesQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<CursorResponse<SearchExportEntityDto>, Error>> Handle(
        ExportSearchIssuesQuery query,
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
                3 AS entity_type,
                i.id AS entity_id,
                i.title,
                NULL::text AS description,
                ARRAY[i.access_type]::text[] AS access_types,
                COALESCE(project_parent.course_id, module_parent.course_id) AS course_id,
                COALESCE(project_parent.course_slug, module_parent.course_slug) AS course_slug,
                COALESCE(project_parent.course_title, module_parent.course_title) AS course_title,
                i.project_id,
                p.title AS project_title,
                module_parent.module_id,
                module_parent.module_title,
                i.updated_at,
                i.author_id
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
              AND (@CursorUpdatedAt::timestamptz IS NULL
                   OR (i.updated_at, i.id) > (@CursorUpdatedAt::timestamptz, @CursorEntityId::uuid))
            ORDER BY i.updated_at, i.id
            LIMIT @LimitPlusOne;

            SELECT COUNT(*)::bigint
            FROM issues i
            WHERE i.status = 'PUBLISHED';
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
