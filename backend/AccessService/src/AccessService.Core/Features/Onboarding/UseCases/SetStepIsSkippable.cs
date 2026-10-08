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

public sealed class SetStepIsSkippableEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/access/plans/{planId:guid}/onboarding-flow/steps/{stepId:guid}/skippable/",
                async Task<EndpointResult<bool>> (
                    [FromRoute] Guid planId,
                    [FromRoute] Guid stepId,
                    [FromBody] SetStepIsSkippableRequest request,
                    [FromServices] SetStepIsSkippableHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(
                        new SetStepIsSkippableCommand(planId, stepId, request.IsSkippable), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record SetStepIsSkippableCommand(
    Guid PlanId,
    Guid StepId,
    bool IsSkippable) : ICommand;

public sealed class SetStepIsSkippableHandler : ICommandHandler<bool, SetStepIsSkippableCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public SetStepIsSkippableHandler(
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

    public async Task<Result<bool, Error>> Handle(SetStepIsSkippableCommand cmd, CancellationToken ct)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == cmd.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        if (!_user.IsOwnerOrAdmin(planResult.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(
            f => f.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        UnitResult<Error> result = flowResult.Value.SetStepIsSkippable(
            cmd.StepId, cmd.IsSkippable, _time.GetUtcNow());
        if (result.IsFailure) return result.Error;

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;
        return cmd.IsSkippable;
    }
}
