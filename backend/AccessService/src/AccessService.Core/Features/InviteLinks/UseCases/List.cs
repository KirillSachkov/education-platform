using AccessService.Contracts.InviteLinks.Dtos;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.InviteLinks.UseCases;

public sealed class ListInviteLinksEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/plans/{planId:guid}/invites/", async Task<EndpointResult<IReadOnlyList<InviteLinkDto>>> (
                [FromRoute] Guid planId,
                [FromServices] ListInviteLinksHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new ListInviteLinksQuery(planId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record ListInviteLinksQuery(Guid PlanId) : IQuery;

public sealed class ListInviteLinksHandler
    : IQueryHandlerWithResult<IReadOnlyList<InviteLinkDto>, ListInviteLinksQuery>
{
    private readonly IInviteLinksRepository _invites;
    private readonly IPlansRepository _plans;
    private readonly UserScopedData _user;

    public ListInviteLinksHandler(
        IInviteLinksRepository invites,
        IPlansRepository plans,
        UserScopedData user)
    {
        _invites = invites;
        _plans = plans;
        _user = user;
    }

    public async Task<Result<IReadOnlyList<InviteLinkDto>, Error>> Handle(
        ListInviteLinksQuery query,
        CancellationToken cancellationToken = default)
    {
        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.Id == query.PlanId, cancellationToken);
        if (getPlan.IsFailure)
        {
            return getPlan.Error;
        }

        Plan plan = getPlan.Value;

        if (!_user.IsOwnerOrAdmin(plan.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        IReadOnlyList<InviteLink> invites = await _invites.GetManyByAsync(
            i => i.PlanId == plan.Id, cancellationToken);

        IReadOnlyList<InviteLinkDto> dtos = invites
            .OrderByDescending(i => i.CreatedAt)
            .Select(CreateInviteLinkHandler.MapToDto)
            .ToList();

        return Result.Success<IReadOnlyList<InviteLinkDto>, Error>(dtos);
    }
}
