using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ProgressLookup;

/// <summary>
/// S2S lookup used by AccessService to resolve author-filtered course sets and legacy
/// author-scoped FREE grants. Returns every non-archived course id of an author.
/// </summary>
public sealed record GetAuthorCourseIdsQuery(Guid AuthorId) : IQuery;

public sealed class GetAuthorCourseIdsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/progress/courses/by-author/{authorId:guid}/ids",
                async Task<EndpointResult<IReadOnlyList<Guid>>> (
                    [FromRoute] Guid authorId,
                    [FromServices] GetAuthorCourseIdsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetAuthorCourseIdsQuery(authorId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetAuthorCourseIdsHandler
    : IQueryHandlerWithResult<IReadOnlyList<Guid>, GetAuthorCourseIdsQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetAuthorCourseIdsHandler(ITransactionManager transactionManager) =>
        _transactionManager = transactionManager;

    public async Task<Result<IReadOnlyList<Guid>, Error>> Handle(
        GetAuthorCourseIdsQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT id
            FROM courses
            WHERE author_id = @AuthorId
              AND status <> 'ARCHIVED'
            ORDER BY id;
            """;

        IEnumerable<Guid> ids = await connection.QueryAsync<Guid>(sql, new { query.AuthorId });
        return Result.Success<IReadOnlyList<Guid>, Error>(ids.ToList());
    }
}

/// <summary>
/// S2S lookup used by AccessService to expand global FULL_ALL / LEARN_ALL grants.
/// Returns every non-archived course id on the platform.
/// </summary>
public sealed record GetAllCourseIdsQuery : IQuery;

public sealed class GetAllCourseIdsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/progress/courses/ids",
                async Task<EndpointResult<IReadOnlyList<Guid>>> (
                    [FromServices] GetAllCourseIdsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetAllCourseIdsQuery(), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetAllCourseIdsHandler
    : IQueryHandlerWithResult<IReadOnlyList<Guid>, GetAllCourseIdsQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetAllCourseIdsHandler(ITransactionManager transactionManager) =>
        _transactionManager = transactionManager;

    public async Task<Result<IReadOnlyList<Guid>, Error>> Handle(
        GetAllCourseIdsQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT id
            FROM courses
            WHERE status <> 'ARCHIVED'
            ORDER BY id;
            """;

        IEnumerable<Guid> ids = await connection.QueryAsync<Guid>(sql);
        return Result.Success<IReadOnlyList<Guid>, Error>(ids.ToList());
    }
}
