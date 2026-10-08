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

/// <summary>
///     Hard-delete invite-link — TG-style: ссылка пропадает из списка. Уже
///     активированные grant'ы не трогаются (это отдельные сущности с собственным
///     lifecycle через <c>POST /access/grants/{id}/revoke</c>).
/// </summary>
public sealed class DeleteInviteLinkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/access/invites/{inviteId:guid}/", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid inviteId,
                [FromServices] DeleteInviteLinkHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new DeleteInviteLinkCommand(inviteId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record DeleteInviteLinkCommand(Guid InviteId) : ICommand;

public sealed class DeleteInviteLinkHandler : ICommandHandler<Guid, DeleteInviteLinkCommand>
{
    private readonly IInviteLinksRepository _invites;
    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<DeleteInviteLinkHandler> _logger;

    public DeleteInviteLinkHandler(
        IInviteLinksRepository invites,
        IPlansRepository plans,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<DeleteInviteLinkHandler> logger)
    {
        _invites = invites;
        _plans = plans;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(DeleteInviteLinkCommand cmd, CancellationToken ct)
    {
        Result<InviteLink, Error> getInvite = await _invites.GetByAsync(
            i => i.Id == cmd.InviteId, ct);
        if (getInvite.IsFailure) return getInvite.Error;

        InviteLink invite = getInvite.Value;

        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.Id == invite.PlanId, ct);
        if (getPlan.IsFailure) return getPlan.Error;

        if (!_user.IsOwnerOrAdmin(getPlan.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        _invites.Remove(invite);

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        _logger.LogInformation(
            "Invite {InviteId} deleted from plan {PlanId} by {UserId}",
            invite.Id, getPlan.Value.Id, _user.UserId);

        return invite.Id;
    }
}
