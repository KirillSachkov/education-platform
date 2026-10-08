using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.HomePins;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.HomePins.UseCases;

/// <summary>Удаляет закреп материала из плана. Epic #397.</summary>
public sealed class DeleteHomePinEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/access/plans/{planId:guid}/home-pins/{pinId:guid}/", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromRoute] Guid pinId,
                [FromServices] DeleteHomePinHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new DeleteHomePinCommand(planId, pinId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record DeleteHomePinCommand(Guid PlanId, Guid PinId) : ICommand;

public sealed class DeleteHomePinHandler : ICommandHandler<Guid, DeleteHomePinCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanPinnedMaterialRepository _pins;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;

    public DeleteHomePinHandler(
        IPlansRepository plans,
        IPlanPinnedMaterialRepository pins,
        ITransactionManager transactions,
        UserScopedData user)
    {
        _plans = plans;
        _pins = pins;
        _transactions = transactions;
        _user = user;
    }

    public async Task<Result<Guid, Error>> Handle(DeleteHomePinCommand cmd, CancellationToken ct)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == cmd.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        if (!_user.IsOwnerOrAdmin(planResult.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        Result<PlanPinnedMaterial, Error> pinResult = await _pins.GetByIdAsync(cmd.PinId, ct);
        if (pinResult.IsFailure) return pinResult.Error;
        if (pinResult.Value.PlanId != cmd.PlanId)
        {
            return HomePinErrors.PinNotFound();
        }

        _pins.Remove(pinResult.Value);

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        return cmd.PinId;
    }
}
