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

public sealed class DeleteOnboardingStepEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/access/plans/{planId:guid}/onboarding-flow/steps/{stepId:guid}/",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid planId,
                    [FromRoute] Guid stepId,
                    [FromServices] DeleteOnboardingStepHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new DeleteOnboardingStepCommand(planId, stepId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record DeleteOnboardingStepCommand(Guid PlanId, Guid StepId) : ICommand;

public sealed class DeleteOnboardingStepHandler : ICommandHandler<Guid, DeleteOnboardingStepCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public DeleteOnboardingStepHandler(
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

    public async Task<Result<Guid, Error>> Handle(DeleteOnboardingStepCommand cmd, CancellationToken ct)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == cmd.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        if (!_user.IsOwnerOrAdmin(planResult.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(f => f.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        UnitResult<Error> remove = flowResult.Value.RemoveStep(cmd.StepId, _time.GetUtcNow());
        if (remove.IsFailure) return remove.Error;

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;
        return cmd.StepId;
    }
}
