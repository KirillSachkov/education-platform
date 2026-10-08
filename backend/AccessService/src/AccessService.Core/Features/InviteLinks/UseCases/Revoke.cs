using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.InviteLinks.UseCases;

public sealed class RevokeInviteLinkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/invites/{inviteId:guid}/revoke", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid inviteId,
                [FromServices] RevokeInviteLinkHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new RevokeInviteLinkCommand(inviteId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record RevokeInviteLinkCommand(Guid InviteId) : ICommand;

public sealed class RevokeInviteLinkHandler : ICommandHandler<Guid, RevokeInviteLinkCommand>
{
    private readonly IInviteLinksRepository _invites;
    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<RevokeInviteLinkHandler> _logger;

    public RevokeInviteLinkHandler(
        IInviteLinksRepository invites,
        IPlansRepository plans,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<RevokeInviteLinkHandler> logger)
    {
        _invites = invites;
        _plans = plans;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        RevokeInviteLinkCommand command,
        CancellationToken cancellationToken)
    {
        Result<InviteLink, Error> getInvite = await _invites.GetByAsync(
            i => i.Id == command.InviteId, cancellationToken);
        if (getInvite.IsFailure)
        {
            return getInvite.Error;
        }

        InviteLink invite = getInvite.Value;

        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.Id == invite.PlanId, cancellationToken);
        if (getPlan.IsFailure)
        {
            return getPlan.Error;
        }

        Plan plan = getPlan.Value;

        if (!_user.IsOwnerOrAdmin(plan.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        if (!invite.IsActive)
        {
            return invite.Id;
        }

        invite.Revoke();

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Invite {InviteId} revoked for plan {PlanId} by {UserId}",
            invite.Id, plan.Id, _user.UserId);

        return invite.Id;
    }
}
