using System.Data.Common;
using AuthService.Contracts.AuthorSpaces;
using AuthService.Domain.AuthorSpaces;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.AuthorSpaces.Queries;

public sealed record GetAuthorSpaceByAuthorIdQuery(Guid AuthorId) : IQuery;

public sealed class GetAuthorSpaceByAuthorIdEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/internal/users/{authorId:guid}/author-space",
                async Task<EndpointResult<AuthorSpaceRouteResponse>> (
                    [FromRoute] Guid authorId,
                    [FromServices] GetAuthorSpaceByAuthorIdHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetAuthorSpaceByAuthorIdQuery(authorId), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
}

public sealed class GetAuthorSpaceByAuthorIdHandler
    : IQueryHandlerWithResult<AuthorSpaceRouteResponse, GetAuthorSpaceByAuthorIdQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetAuthorSpaceByAuthorIdHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<AuthorSpaceRouteResponse, Error>> Handle(
        GetAuthorSpaceByAuthorIdQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               s.id AS AuthorId,
                               s.slug AS Slug
                           FROM author_spaces s
                           WHERE s.id = @AuthorId
                           """;

        AuthorSpaceRouteResponse? row = await connection.QuerySingleOrDefaultAsync<AuthorSpaceRouteResponse>(
            new CommandDefinition(sql, new { query.AuthorId }, cancellationToken: cancellationToken));

        return row is null
            ? AuthorSpaceErrors.NotFound(query.AuthorId)
            : row;
    }
}
