using System.Data.Common;
using AuthService.Contracts;
using AuthService.Contracts.AuthorSpaces;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.AuthorSpaces.Queries;

public sealed record GetAllAuthorSpacesQuery(string? Cursor, int Limit = 50) : IQuery;

public sealed class GetAllAuthorSpacesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/author-spaces/",
                async Task<EndpointResult<CursorResponse<AuthorSpaceListItem>>> (
                    [FromQuery] string? cursor,
                    [FromQuery] int limit,
                    [FromServices] GetAllAuthorSpacesHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(
                    new GetAllAuthorSpacesQuery(cursor, limit == 0 ? 50 : limit), ct))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
}

public sealed class GetAllAuthorSpacesHandler
    : IQueryHandlerWithResult<CursorResponse<AuthorSpaceListItem>, GetAllAuthorSpacesQuery>
{
    private const int MAX_LIMIT = 200;
    private readonly ITransactionManager _transactionManager;

    public GetAllAuthorSpacesHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<CursorResponse<AuthorSpaceListItem>, Error>> Handle(
        GetAllAuthorSpacesQuery query,
        CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(query.Limit, 1, MAX_LIMIT);
        Cursor? cursor = Cursor.Decode(query.Cursor);

        DbConnection connection = _transactionManager.GetDbConnection();

        var parameters = new
        {
            CursorCreatedAt = cursor?.CreatedAt,
            CursorId = cursor?.LastId,
            Limit = limit + 1,
        };

        const string countSql = "SELECT COUNT(*) FROM author_spaces;";

        const string dataSql = """
                               SELECT
                                   s.id AS AuthorId,
                                   s.slug AS Slug,
                                   u.display_name AS DisplayName,
                                   s.tagline AS Tagline,
                                   up.avatar_id AS AvatarId,
                                   s.created_at
                               FROM author_spaces s
                               JOIN users u ON u.id = s.id
                               LEFT JOIN user_profiles up ON up.id = s.id
                               WHERE (@CursorCreatedAt IS NULL OR (s.created_at, s.id) > (@CursorCreatedAt, @CursorId))
                               ORDER BY s.created_at, s.id
                               LIMIT @Limit
                               """;

        long totalCount = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(countSql, cancellationToken: cancellationToken));

        List<AuthorSpaceRow> rows = (await connection.QueryAsync<AuthorSpaceRow>(
            new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken))).ToList();

        bool hasMore = rows.Count > limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        List<AuthorSpaceListItem> items = rows
            .Select(r => new AuthorSpaceListItem(r.AuthorId, r.Slug, r.DisplayName, r.Tagline, r.AvatarId))
            .ToList();

        string? nextCursor = hasMore && rows.Count > 0
            ? Cursor.Encode(rows[^1].CreatedAt, rows[^1].AuthorId)
            : null;

        return new CursorResponse<AuthorSpaceListItem>
        {
            Items = items,
            NextCursor = nextCursor,
            TotalCount = totalCount,
        };
    }

    private sealed class AuthorSpaceRow
    {
        public Guid AuthorId { get; init; }
        public string Slug { get; init; } = null!;
        public string? DisplayName { get; init; }
        public string? Tagline { get; init; }
        public Guid? AvatarId { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
