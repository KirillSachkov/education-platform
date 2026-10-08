using AccessService.Contracts.HomePins;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.HomePins;
using Core.Abstractions;
using Core.Database;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Materials;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.HomePins.UseCases;

/// <summary>
///     Закрепляет материал в плане. 404 если материала нет (проверка через ECS summaries),
///     409 если пара (plan, material) уже закреплена. Закреп добавляется в конец списка.
///     Epic #397.
/// </summary>
public sealed class AddHomePinEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/{planId:guid}/home-pins/", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromBody] AddHomePinRequest request,
                [FromServices] AddHomePinHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new AddHomePinCommand(planId, request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record AddHomePinCommand(Guid PlanId, AddHomePinRequest Request) : ICommand;

public sealed class AddHomePinHandler : ICommandHandler<Guid, AddHomePinCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanPinnedMaterialRepository _pins;
    private readonly IEducationContentServiceClient _ecs;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public AddHomePinHandler(
        IPlansRepository plans,
        IPlanPinnedMaterialRepository pins,
        IEducationContentServiceClient ecs,
        ITransactionManager transactions,
        UserScopedData user,
        TimeProvider time)
    {
        _plans = plans;
        _pins = pins;
        _ecs = ecs;
        _transactions = transactions;
        _user = user;
        _time = time;
    }

    public async Task<Result<Guid, Error>> Handle(AddHomePinCommand cmd, CancellationToken ct)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == cmd.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        if (!_user.IsOwnerOrAdmin(planResult.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        Guid materialId = cmd.Request.MaterialId;

        // 404 если материала нет — проверяем через ECS summaries (S2S, без entitlement-чека).
        Result<IReadOnlyList<MaterialSummaryDto>, Error> summaries =
            await _ecs.GetMaterialSummariesAsync([materialId], ct);
        if (summaries.IsFailure) return summaries.Error;
        if (summaries.Value.All(s => s.Id != materialId))
        {
            return HomePinErrors.MaterialNotFound();
        }

        // 409 если уже закреплён.
        if (await _pins.ExistsAsync(cmd.PlanId, materialId, ct))
        {
            return HomePinErrors.AlreadyPinned();
        }

        DateTimeOffset now = _time.GetUtcNow();

        // SortKey после текущего последнего закрепа.
        IReadOnlyList<PlanPinnedMaterial> existing = await _pins.GetByPlanAsync(cmd.PlanId, ct);
        SortKey sortKey = existing.Count == 0
            ? SortKey.Initial()
            : SortKey.After(existing[^1].SortKey);

        Result<PlanPinnedMaterial, Error> pinResult = PlanPinnedMaterial.Create(
            cmd.PlanId, materialId, cmd.Request.Note, sortKey, now);
        if (pinResult.IsFailure) return pinResult.Error;

        await _pins.AddAsync(pinResult.Value, ct);

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        return pinResult.Value.Id;
    }
}
