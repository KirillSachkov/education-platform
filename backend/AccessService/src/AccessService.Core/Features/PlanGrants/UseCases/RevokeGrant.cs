using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Core.Database;
using AccessService.Core.Features.Plans;
using AccessService.Domain;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.PlanGrants.UseCases;

public sealed class RevokeGrantEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/grants/{grantId:guid}/revoke", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid grantId,
                [FromBody] RevokeGrantRequest? request,
                [FromServices] RevokeGrantHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new RevokeGrantCommand(grantId, request?.Reason), ct))
            .RequirePermissions(PlatformPermissions.Plans.GRANT);
    }
}

public sealed record RevokeGrantCommand(Guid GrantId, string? Reason) : ICommand;

public sealed class RevokeGrantHandler : ICommandHandler<Guid, RevokeGrantCommand>
{
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<RevokeGrantHandler> _logger;

    public RevokeGrantHandler(
        IPlanGrantsRepository grants,
        IPlansRepository plans,
        IOutboxService outbox,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<RevokeGrantHandler> logger)
    {
        _grants = grants;
        _plans = plans;
        _outbox = outbox;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        RevokeGrantCommand command,
        CancellationToken cancellationToken)
    {
        Result<PlanGrant, Error> getGrant = await _grants.GetByAsync(
            g => g.Id == command.GrantId, cancellationToken);
        if (getGrant.IsFailure)
        {
            return getGrant.Error;
        }

        PlanGrant grant = getGrant.Value;

        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.Id == grant.PlanId, cancellationToken);
        if (getPlan.IsFailure)
        {
            return AccessErrors.PlanNotFound();
        }

        Plan plan = getPlan.Value;
        if (!_user.IsOwnerOrAdmin(plan.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        Guid? canonicalTelegramPlanId = await CanonicalTelegramPlanResolver.ResolveAsync(
            plan,
            _plans,
            cancellationToken);

        UnitResult<Error> revoke = grant.Revoke(_user.UserId, command.Reason);
        if (revoke.IsFailure)
        {
            return revoke.Error;
        }

        await _outbox.PublishAsync(new PlanGrantRevoked(
            grant.Id,
            grant.UserId,
            grant.PlanId,
            command.Reason,
            grant.RevokedAt!.Value,
            canonicalTelegramPlanId));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _logger.LogInformation(
            "Plan grant {GrantId} revoked by {UserId} (plan {PlanId}, reason {Reason})",
            grant.Id, _user.UserId, plan.Id, command.Reason);

        return grant.Id;
    }
}
