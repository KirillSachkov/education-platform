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

namespace AccessService.Core.Features.Plans.UseCases;

/// <summary>
/// <c>DELETE /access/plans/{planId}/promotion/</c> — снимает акцию с плана (идемпотентно).
/// Tier-1: <c>plans.manage</c>; Tier-2: ownership.
/// </summary>
public sealed class ClearPlanPromotionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/access/plans/{planId:guid}/promotion/", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromServices] ClearPlanPromotionHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new ClearPlanPromotionCommand(planId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record ClearPlanPromotionCommand(Guid PlanId) : ICommand;

public sealed class ClearPlanPromotionHandler : ICommandHandler<Guid, ClearPlanPromotionCommand>
{
    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<ClearPlanPromotionHandler> _logger;

    public ClearPlanPromotionHandler(
        IPlansRepository plans,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<ClearPlanPromotionHandler> logger)
    {
        _plans = plans;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(ClearPlanPromotionCommand command, CancellationToken cancellationToken)
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

        plan.ClearPromotion();

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation("Promotion cleared on plan {PlanId} by {UserId}", plan.Id, _user.UserId);

        return plan.Id;
    }
}
