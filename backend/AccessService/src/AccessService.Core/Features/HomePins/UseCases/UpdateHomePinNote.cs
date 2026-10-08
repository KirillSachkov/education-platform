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
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.HomePins.UseCases;

/// <summary>Обновляет заметку у закрепа. Epic #397.</summary>
public sealed class UpdateHomePinNoteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/access/plans/{planId:guid}/home-pins/{pinId:guid}/", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromRoute] Guid pinId,
                [FromBody] UpdateHomePinNoteRequest request,
                [FromServices] UpdateHomePinNoteHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new UpdateHomePinNoteCommand(planId, pinId, request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record UpdateHomePinNoteCommand(Guid PlanId, Guid PinId, UpdateHomePinNoteRequest Request) : ICommand;

public sealed class UpdateHomePinNoteHandler : ICommandHandler<Guid, UpdateHomePinNoteCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanPinnedMaterialRepository _pins;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public UpdateHomePinNoteHandler(
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

    public async Task<Result<Guid, Error>> Handle(UpdateHomePinNoteCommand cmd, CancellationToken ct)
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

        UnitResult<Error> update = pinResult.Value.UpdateNote(cmd.Request.Note, _time.GetUtcNow());
        if (update.IsFailure) return update.Error;

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        return cmd.PinId;
    }
}
