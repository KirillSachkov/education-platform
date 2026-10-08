using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Onboarding.UseCases;

public sealed class ReturnToOnboardingStepEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/onboarding/{planId:guid}/steps/{stepId:guid}/return-to/",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid planId,
                    [FromRoute] Guid stepId,
                    [FromServices] ReturnToOnboardingStepHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new ReturnToOnboardingStepCommand(planId, stepId), ct))
            .RequireAuthorization();
    }
}

public sealed record ReturnToOnboardingStepCommand(Guid PlanId, Guid StepId) : ICommand;

public sealed class ReturnToOnboardingStepHandler : ICommandHandler<Guid, ReturnToOnboardingStepCommand>
{
    private readonly IUserPlanOnboardingsRepository _onboardings;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;

    public ReturnToOnboardingStepHandler(
        IUserPlanOnboardingsRepository onboardings,
        IPlanOnboardingFlowsRepository flows,
        ITransactionManager transactions,
        UserScopedData user)
    {
        _onboardings = onboardings;
        _flows = flows;
        _transactions = transactions;
        _user = user;
    }

    public async Task<Result<Guid, Error>> Handle(ReturnToOnboardingStepCommand cmd, CancellationToken ct)
    {
        UserPlanOnboarding? onboarding = await _onboardings.GetAsync(_user.UserId, cmd.PlanId, ct);
        if (onboarding is null) return OnboardingErrors.OnboardingNotFound();

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(
            f => f.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        PlanOnboardingStep? step = flowResult.Value.Steps.FirstOrDefault(s => s.Id == cmd.StepId);
        if (step is null) return OnboardingErrors.StepNotFound();

        UnitResult<Error> revert = onboarding.ReturnToStep(cmd.StepId);
        if (revert.IsFailure) return revert.Error;

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        return cmd.StepId;
    }
}
