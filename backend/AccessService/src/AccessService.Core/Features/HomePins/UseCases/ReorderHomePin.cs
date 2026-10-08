using AccessService.Contracts.HomePins;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.HomePins;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.HomePins.UseCases;

/// <summary>
///     Переупорядочивает закреп — fractional reorder между указанными соседями
///     (зеркалит ReorderStep онбординга). Epic #397.
/// </summary>
public sealed class ReorderHomePinEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/{planId:guid}/home-pins/{pinId:guid}/order/", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromRoute] Guid pinId,
                [FromBody] ReorderHomePinRequest request,
                [FromServices] ReorderHomePinHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new ReorderHomePinCommand(planId, pinId, request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record ReorderHomePinCommand(Guid PlanId, Guid PinId, ReorderHomePinRequest Request) : ICommand;

public sealed class ReorderHomePinHandler : ICommandHandler<Guid, ReorderHomePinCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanPinnedMaterialRepository _pins;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public ReorderHomePinHandler(
        IPlansRepository plans,
        IPlanPinnedMaterialRepository pins,
        ITransactionManager transactions,
        UserScopedData user,
        TimeProvider time)
    {
        _plans = plans;
        _pins = pins;
        _transactions = transactions;
        _user = user;
        _time = time;
    }

    public async Task<Result<Guid, Error>> Handle(ReorderHomePinCommand cmd, CancellationToken ct)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == cmd.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        if (!_user.IsOwnerOrAdmin(planResult.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        IReadOnlyList<PlanPinnedMaterial> pins = await _pins.GetByPlanAsync(cmd.PlanId, ct);

        PlanPinnedMaterial? pin = pins.FirstOrDefault(p => p.Id == cmd.PinId);
        if (pin is null)
        {
            return HomePinErrors.PinNotFound();
        }

        SortKey? before = null;
        SortKey? after = null;

        if (cmd.Request.BeforeId.HasValue)
        {
            PlanPinnedMaterial? b = pins.FirstOrDefault(p => p.Id == cmd.Request.BeforeId.Value);
            if (b is null) return HomePinErrors.PinNotFound();
            before = b.SortKey;
        }

        if (cmd.Request.AfterId.HasValue)
        {
            PlanPinnedMaterial? a = pins.FirstOrDefault(p => p.Id == cmd.Request.AfterId.Value);
            if (a is null) return HomePinErrors.PinNotFound();
            after = a.SortKey;
        }

        Result<SortKey, Error> newKey = SortKey.Between(before, after);
        if (newKey.IsFailure) return newKey.Error;

        pin.Reorder(newKey.Value, _time.GetUtcNow());

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        return cmd.PinId;
    }
}
