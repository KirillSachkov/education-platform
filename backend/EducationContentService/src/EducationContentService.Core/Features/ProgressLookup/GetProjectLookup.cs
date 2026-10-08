using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.ProgressLookup;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ProgressLookup;

public sealed record GetProjectLookupQuery(Guid CourseId, Guid ProjectId) : IQuery;

public sealed class GetProjectLookupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/progress/courses/{courseId:guid}/projects/{projectId:guid}",
            async Task<EndpointResult<ProjectDto>> (
                [FromRoute] Guid courseId,
                [FromRoute] Guid projectId,
                [FromServices] GetProjectLookupHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetProjectLookupQuery(courseId, projectId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetProjectLookupHandler : IQueryHandlerWithResult<ProjectDto, GetProjectLookupQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetProjectLookupHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<ProjectDto, Error>> Handle(
        GetProjectLookupQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM project_items pi WHERE pi.project_id = p.id) AS project_issues_total
            FROM projects p
            INNER JOIN course_items ci
                ON ci.reference_id = p.id AND ci.item_type = 'Project'
            WHERE p.id = @ProjectId
              AND ci.course_id = @CourseId;
            """;

        int? projectIssuesTotal = await connection.QueryFirstOrDefaultAsync<int?>(sql, new
        {
            query.ProjectId,
            query.CourseId
        });

        if (projectIssuesTotal is null)
            return GeneralErrors.NotFound(query.ProjectId);

        return new ProjectDto(projectIssuesTotal.Value);
    }
}
