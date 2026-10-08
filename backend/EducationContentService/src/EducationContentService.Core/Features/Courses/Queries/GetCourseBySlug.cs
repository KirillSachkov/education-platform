using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Courses.Queries;

public sealed record CourseSlugResolveDto(Guid CourseId, string Slug);

public sealed record GetCourseBySlugQuery(string Slug) : IQuery;

public sealed class GetCourseBySlugEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/by-slug/{slug}", async Task<EndpointResult<CourseSlugResolveDto>> (
                [FromRoute] string slug,
                [FromServices] GetCourseBySlugHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new GetCourseBySlugQuery(slug), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetCourseBySlugHandler : IQueryHandlerWithResult<CourseSlugResolveDto, GetCourseBySlugQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetCourseBySlugHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<CourseSlugResolveDto, Error>> Handle(
        GetCourseBySlugQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT id AS CourseId, slug AS Slug
                           FROM courses
                           WHERE slug = @Slug
                           LIMIT 1;
                           """;

        var command = new CommandDefinition(sql, new { Slug = query.Slug.ToLowerInvariant() }, cancellationToken: cancellationToken);
        CourseSlugResolveDto? result = await connection.QueryFirstOrDefaultAsync<CourseSlugResolveDto>(command);

        return result is null
            ? GeneralErrors.NotFound()
            : result;
    }
}
