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

public sealed record GetProjectSearchLookupQuery(Guid ProjectId) : IQuery;

public sealed class GetProjectSearchLookupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/search/projects/{projectId:guid}", async Task<EndpointResult<ProjectSearchLookupDto>> (
                [FromRoute] Guid projectId,
                [FromServices] GetProjectSearchLookupHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetProjectSearchLookupQuery(projectId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetProjectSearchLookupHandler
    : IQueryHandlerWithResult<ProjectSearchLookupDto, GetProjectSearchLookupQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetProjectSearchLookupHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<ProjectSearchLookupDto, Error>> Handle(
        GetProjectSearchLookupQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                p.id,
                p.author_id,
                p.title,
                p.description,
                p.status,
                p.updated_at,
                c.id AS course_id,
                c.slug AS course_slug,
                c.title AS course_title,
                COALESCE(
                    ARRAY_AGG(i.access_type) FILTER (WHERE i.access_type IS NOT NULL),
                    ARRAY[]::text[]
                ) AS item_access_types
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
            WHERE p.id = @ProjectId
            GROUP BY
                p.id,
                p.author_id,
                p.title,
                p.description,
                p.status,
                p.updated_at,
                c.id,
                c.slug,
                c.title;
            """;

        ProjectSearchLookupRow? row = await connection.QueryFirstOrDefaultAsync<ProjectSearchLookupRow>(
            new CommandDefinition(sql, new { query.ProjectId }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return GeneralErrors.NotFound(query.ProjectId);
        }

        return new ProjectSearchLookupDto(
            row.Id,
            row.CourseId,
            row.CourseSlug,
            row.Title,
            row.Description,
            row.CourseTitle,
            SearchLookupEnumConverter.ToPublicationStatus(row.Status),
            row.UpdatedAt,
            row.ItemAccessTypes.Length == 0
                ? ContentAccessTagBuilder.BuildCoursePublicDefault()
                : ContentAccessTagBuilder.BuildMany(
                    row.ItemAccessTypes,
                    row.Id,
                    row.CourseId.HasValue ? [row.CourseId.Value] : []),
            row.AuthorId);
    }

    private sealed class ProjectSearchLookupRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public Guid? CourseId { get; init; }
        public string? CourseSlug { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public string? CourseTitle { get; init; }
        public string Status { get; init; } = null!;
        public DateTime UpdatedAt { get; init; }
        public string[] ItemAccessTypes { get; init; } = [];
    }
}
