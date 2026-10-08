using AccessService.Contracts.Onboarding;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Onboarding.UseCases;

public sealed class ReorderOnboardingStepEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/access/plans/{planId:guid}/onboarding-flow/steps/{stepId:guid}/order/",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid planId,
                    [FromRoute] Guid stepId,
                    [FromBody] ReorderStepRequest request,
                    [FromServices] ReorderOnboardingStepHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new ReorderOnboardingStepCommand(planId, stepId, request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record ReorderOnboardingStepCommand(
    Guid PlanId, Guid StepId, ReorderStepRequest Request) : ICommand;

public sealed class ReorderOnboardingStepHandler : ICommandHandler<Guid, ReorderOnboardingStepCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public ReorderOnboardingStepHandler(
        IPlansRepository plans,
        IPlanOnboardingFlowsRepository flows,
        ITransactionManager transactions,
        UserScopedData user,
        TimeProvider time)
    {
        _plans = plans;
        _flows = flows;
        _transactions = transactions;
        _user = user;
        _time = time;
    }

    public async Task<Result<Guid, Error>> Handle(ReorderOnboardingStepCommand cmd, CancellationToken ct)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == cmd.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        if (!_user.IsOwnerOrAdmin(planResult.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(f => f.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        UnitResult<Error> reorder = flowResult.Value.ReorderStep(
            cmd.StepId, cmd.Request.BeforeStepId, cmd.Request.AfterStepId, _time.GetUtcNow());
        if (reorder.IsFailure) return reorder.Error;

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;
        return cmd.StepId;
    }
}
