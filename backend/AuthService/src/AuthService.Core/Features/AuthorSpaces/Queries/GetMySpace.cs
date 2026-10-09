using AuthService.Contracts.AuthorSpaces;
using AuthService.Core.Database;
using AuthService.Domain.AuthorSpaces;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.AuthorSpaces.Queries;

public sealed record GetMyAuthorSpaceQuery : IQuery;

public sealed class GetMyAuthorSpaceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/me/author-space", async Task<EndpointResult<AuthorSpaceDetailResponse>> (
                    [FromServices] GetMyAuthorSpaceHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetMyAuthorSpaceQuery(), ct))
            .RequirePermissions(PlatformPermissions.Profiles.AUTHOR);
}

public sealed class GetMyAuthorSpaceHandler
    : IQueryHandlerWithResult<AuthorSpaceDetailResponse, GetMyAuthorSpaceQuery>
{
    private readonly IAuthorSpaceRepository _repository;
    private readonly UserScopedData _user;

    public GetMyAuthorSpaceHandler(
        IAuthorSpaceRepository repository,
        UserScopedData user)
    {
        _repository = repository;
        _user = user;
    }

    public async Task<Result<AuthorSpaceDetailResponse, Error>> Handle(
        GetMyAuthorSpaceQuery query,
        CancellationToken cancellationToken)
    {
        AuthorSpace? space = await _repository.GetByAsync(
            s => s.Id == _user.UserId, cancellationToken);

        if (space is null)
            return AuthorSpaceErrors.NotFound(_user.UserId);

        AuthorSpaceFeatureFlags flags = space.FeatureFlags;

        return new AuthorSpaceDetailResponse(
            space.Id,
            space.Slug.Value,
            space.Tagline?.Value,
            space.LogoAssetId,
            new AuthorSpaceFeatureFlagsDto(
                flags.GitHubIntegration,
                flags.PrReviews,
                flags.AiAssistant,
                flags.Roadmaps,
                flags.CustomLanding),
            space.CreatedAt,
            space.UpdatedAt);
    }
}