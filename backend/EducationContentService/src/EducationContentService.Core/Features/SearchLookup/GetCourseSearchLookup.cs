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

public sealed record GetCourseSearchLookupQuery(Guid CourseId) : IQuery;

public sealed class GetCourseSearchLookupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/search/courses/{courseId:guid}", async Task<EndpointResult<CourseSearchLookupDto>> (
                [FromRoute] Guid courseId,
                [FromServices] GetCourseSearchLookupHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetCourseSearchLookupQuery(courseId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetCourseSearchLookupHandler
    : IQueryHandlerWithResult<CourseSearchLookupDto, GetCourseSearchLookupQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetCourseSearchLookupHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<CourseSearchLookupDto, Error>> Handle(
        GetCourseSearchLookupQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                c.id,
                c.slug,
                c.title,
                c.description,
                c.status,
                c.updated_at,
                c.author_id
            FROM courses c
            WHERE c.id = @CourseId;
            """;

        CourseSearchLookupRow? row = await connection.QueryFirstOrDefaultAsync<CourseSearchLookupRow>(
            new CommandDefinition(sql, new { query.CourseId }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return GeneralErrors.NotFound(query.CourseId);
        }

        return new CourseSearchLookupDto(
            row.Id,
            row.Slug,
            row.Title,
            row.Description,
            SearchLookupEnumConverter.ToPublicationStatus(row.Status),
            row.UpdatedAt,
            ContentAccessTagBuilder.BuildCoursePublicDefault(),
            row.AuthorId);
    }

    private sealed class CourseSearchLookupRow
    {
        public Guid Id { get; init; }
        public string Slug { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string Description { get; init; } = null!;
        public string Status { get; init; } = null!;
        public DateTime UpdatedAt { get; init; }
        public Guid AuthorId { get; init; }
    }
}
