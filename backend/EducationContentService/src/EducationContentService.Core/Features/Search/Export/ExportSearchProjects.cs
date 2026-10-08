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

public sealed record ExportSearchProjectsQuery(ExportSearchProjectsRequest Request) : IQuery;
public sealed class ExportSearchProjectsQueryValidator : AbstractValidator<ExportSearchProjectsQuery>
{
    public ExportSearchProjectsQueryValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(1, Constants.MAX_SEARCH_EXPORT_LIMIT)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchProjectsRequest.Limit)));
        RuleFor(x => x.Request.Cursor)
            .Must(cursor => string.IsNullOrWhiteSpace(cursor) || Cursor.Decode(cursor) is not null)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchProjectsRequest.Cursor)));
    }
}

public sealed class ExportSearchProjectsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/search/export/entities/project", async Task<EndpointResult<CursorResponse<SearchExportEntityDto>>> (
                [AsParameters] ExportSearchProjectsRequest request,
                [FromServices] ExportSearchProjectsHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new ExportSearchProjectsQuery(request), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class ExportSearchProjectsHandler
    : IQueryHandlerWithResult<CursorResponse<SearchExportEntityDto>, ExportSearchProjectsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<ExportSearchProjectsQuery> _validator;

    public ExportSearchProjectsHandler(
        ITransactionManager transactionManager,
        IValidator<ExportSearchProjectsQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<CursorResponse<SearchExportEntityDto>, Error>> Handle(
        ExportSearchProjectsQuery query,
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
                2 AS entity_type,
                p.id AS entity_id,
                p.title,
                p.description,
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
                p.updated_at,
                p.author_id
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
              AND (@CursorUpdatedAt::timestamptz IS NULL
                   OR (p.updated_at, p.id) > (@CursorUpdatedAt::timestamptz, @CursorEntityId::uuid))
            GROUP BY
                p.id,
                p.title,
                p.description,
                c.id,
                c.slug,
                c.title,
                p.updated_at,
                p.author_id
            ORDER BY p.updated_at, p.id
            LIMIT @LimitPlusOne;

            SELECT COUNT(*)::bigint
            FROM projects p
            WHERE p.status = 'PUBLISHED';
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
