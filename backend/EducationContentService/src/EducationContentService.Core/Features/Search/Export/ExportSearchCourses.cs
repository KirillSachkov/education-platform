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

public sealed record ExportSearchCoursesQuery(ExportSearchCoursesRequest Request) : IQuery;
public sealed class ExportSearchCoursesQueryValidator : AbstractValidator<ExportSearchCoursesQuery>
{
    public ExportSearchCoursesQueryValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(1, Constants.MAX_SEARCH_EXPORT_LIMIT)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchCoursesRequest.Limit)));
        RuleFor(x => x.Request.Cursor)
            .Must(cursor => string.IsNullOrWhiteSpace(cursor) || Cursor.Decode(cursor) is not null)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ExportSearchCoursesRequest.Cursor)));
    }
}

public sealed class ExportSearchCoursesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/search/export/entities/course", async Task<EndpointResult<CursorResponse<SearchExportEntityDto>>> (
                [AsParameters] ExportSearchCoursesRequest request,
                [FromServices] ExportSearchCoursesHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new ExportSearchCoursesQuery(request), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class ExportSearchCoursesHandler
    : IQueryHandlerWithResult<CursorResponse<SearchExportEntityDto>, ExportSearchCoursesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<ExportSearchCoursesQuery> _validator;

    public ExportSearchCoursesHandler(
        ITransactionManager transactionManager,
        IValidator<ExportSearchCoursesQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<CursorResponse<SearchExportEntityDto>, Error>> Handle(
        ExportSearchCoursesQuery query,
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
                0 AS entity_type,
                c.id AS entity_id,
                c.title,
                c.description,
                ARRAY['PUBLIC']::text[] AS access_types,
                c.id AS course_id,
                c.slug AS course_slug,
                c.title AS course_title,
                NULL::uuid AS project_id,
                NULL::text AS project_title,
                NULL::uuid AS module_id,
                NULL::text AS module_title,
                c.updated_at,
                c.author_id
            FROM courses c
            WHERE c.status = 'PUBLISHED'
              AND (@CursorUpdatedAt::timestamptz IS NULL
                   OR (c.updated_at, c.id) > (@CursorUpdatedAt::timestamptz, @CursorEntityId::uuid))
            ORDER BY c.updated_at, c.id
            LIMIT @LimitPlusOne;

            SELECT COUNT(*)::bigint
            FROM courses c
            WHERE c.status = 'PUBLISHED';
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
