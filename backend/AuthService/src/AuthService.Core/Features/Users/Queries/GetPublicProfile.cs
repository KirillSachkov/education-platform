using System.Data.Common;
using AuthService.Contracts;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Users.Queries;

public sealed record GetPublicProfileQuery(Guid UserId) : IQuery;

public sealed class GetPublicProfileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/{userId:guid}/public-profile", async Task<EndpointResult<PublicProfileResponse>> (
                    [FromRoute] Guid userId,
                    [FromServices] GetPublicProfileHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetPublicProfileQuery(userId), ct))
            .AllowAnonymousEndpoint();
}

public sealed class GetPublicProfileHandler
    : IQueryHandlerWithResult<PublicProfileResponse, GetPublicProfileQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetPublicProfileHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<PublicProfileResponse, Error>> Handle(
        GetPublicProfileQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               u.id AS Id,
                               u.display_name AS DisplayName,
                               u.user_name AS Username,
                               up.bio AS Bio,
                               up.avatar_id AS AvatarId,
                               up.role_profiles AS RoleProfiles
                           FROM users u
                           LEFT JOIN user_profiles up ON up.id = u.id
                           WHERE u.id = @UserId
                           """;

        PublicProfileRow? row = await connection.QuerySingleOrDefaultAsync<PublicProfileRow>(
            sql,
            new { query.UserId });

        if (row is null)
            return GeneralErrors.NotFound(query.UserId);

        string? specialization = null;
        string? aboutAsAuthor = null;
        string? gitHubUrl = null;

        if (row.RoleProfiles is not null)
        {
            specialization = row.RoleProfiles.Author?.Specialization;
            aboutAsAuthor = row.RoleProfiles.Author?.AboutAsAuthor;
            gitHubUrl = row.RoleProfiles.Student?.GitHubUrl;
        }

        return new PublicProfileResponse(
            row.Id,
            row.DisplayName,
            row.Username,
            row.Bio,
            row.AvatarId,
            specialization,
            aboutAsAuthor,
            gitHubUrl);
    }

    private sealed record PublicProfileRow
    {
        public Guid Id { get; init; }
        public string? DisplayName { get; init; }
        public string? Username { get; init; }
        public string? Bio { get; init; }
        public Guid? AvatarId { get; init; }
        public ProfilesDto? RoleProfiles { get; init; }
    }
}
