using AccessService.Contracts.Integrations.GitHub;
using AccessService.Core.Database;
using AccessService.Domain.Integrations.GitHub;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Integrations.GitHubApp.UseCases;

public sealed class GetGithubInvitationStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/integrations/github/invitations/{planId:guid}/",
                async Task<EndpointResult<GithubInvitationStatusResponse?>> (
                    [FromRoute] Guid planId,
                    [FromServices] GetGithubInvitationStatusHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetGithubInvitationStatusQuery(planId), ct))
            .RequireAuthorization();
    }
}

public sealed record GetGithubInvitationStatusQuery(Guid PlanId) : ICommand;

public sealed class GetGithubInvitationStatusHandler
    : ICommandHandler<GithubInvitationStatusResponse?, GetGithubInvitationStatusQuery>
{
    private readonly IGithubOrgInvitationsRepository _invitations;
    private readonly UserScopedData _user;

    public GetGithubInvitationStatusHandler(
        IGithubOrgInvitationsRepository invitations,
        UserScopedData user)
    {
        _invitations = invitations;
        _user = user;
    }

    public async Task<Result<GithubInvitationStatusResponse?, Error>> Handle(
        GetGithubInvitationStatusQuery q, CancellationToken ct)
    {
        GithubOrgInvitation? invitation = await _invitations.GetByUserPlanAsync(_user.UserId, q.PlanId, ct);
        if (invitation is null)
        {
            return (GithubInvitationStatusResponse?)null;
        }

        return CreateGithubInvitationHandler.Map(invitation);
    }
}
