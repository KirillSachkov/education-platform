using AccessService.Contracts.InviteLinks.Dtos;
using AccessService.Contracts.InviteLinks.Requests;
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

public sealed class CreateInviteLinkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/{planId:guid}/invites/", async Task<EndpointResult<InviteLinkDto>> (
                [FromRoute] Guid planId,
                [FromBody] CreateInviteLinkRequest request,
                [FromServices] CreateInviteLinkHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new CreateInviteLinkCommand(planId, request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record CreateInviteLinkCommand(Guid PlanId, CreateInviteLinkRequest Request) : ICommand;

public sealed class CreateInviteLinkHandler : ICommandHandler<InviteLinkDto, CreateInviteLinkCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IInviteLinksRepository _invites;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<CreateInviteLinkHandler> _logger;

    public CreateInviteLinkHandler(
        IPlansRepository plans,
        IInviteLinksRepository invites,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<CreateInviteLinkHandler> logger)
    {
        _plans = plans;
        _invites = invites;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<InviteLinkDto, Error>> Handle(
        CreateInviteLinkCommand command,
        CancellationToken cancellationToken)
    {
        Result<Plan, Error> get = await _plans.GetByAsync(p => p.Id == command.PlanId, cancellationToken);
        if (get.IsFailure)
        {
            return get.Error;
        }

        Plan plan = get.Value;

        if (!_user.IsOwnerOrAdmin(plan.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        if (plan.ArchivedAt is not null)
        {
            return AccessErrors.PlanArchived();
        }

        CreateInviteLinkRequest req = command.Request;
        InviteLink link = InviteLink.Create(
            plan.Id,
            _user.UserId,
            req.MultiUse,
            req.MaxUses,
            req.ExpiresAt,
            req.Label);

        await _invites.AddAsync(link, cancellationToken);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Invite {InviteId} created for plan {PlanId} by {UserId}",
            link.Id, plan.Id, _user.UserId);

        return MapToDto(link);
    }

    internal static InviteLinkDto MapToDto(InviteLink link) => new(
        link.Id,
        link.PlanId,
        link.Token.Value,
        link.CreatedBy,
        link.MultiUse,
        link.MaxUses,
        link.UsageCount,
        link.ExpiresAt,
        link.IsActive,
        link.Label,
        link.CreatedAt,
        link.RevokedAt);
}
