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

public sealed class SkipOnboardingStepEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/onboarding/{planId:guid}/steps/{stepId:guid}/skip/",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid planId,
                    [FromRoute] Guid stepId,
                    [FromServices] SkipOnboardingStepHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new SkipOnboardingStepCommand(planId, stepId), ct))
            .RequireAuthorization();
    }
}

public sealed record SkipOnboardingStepCommand(Guid PlanId, Guid StepId) : ICommand;

public sealed class SkipOnboardingStepHandler : ICommandHandler<Guid, SkipOnboardingStepCommand>
{
    private readonly IUserPlanOnboardingsRepository _onboardings;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;

    public SkipOnboardingStepHandler(
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

    public async Task<Result<Guid, Error>> Handle(SkipOnboardingStepCommand cmd, CancellationToken ct)
    {
        UserPlanOnboarding? onboarding = await _onboardings.GetAsync(_user.UserId, cmd.PlanId, ct);
        if (onboarding is null) return OnboardingErrors.OnboardingNotFound();

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(
            f => f.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        PlanOnboardingStep? step = flowResult.Value.Steps.FirstOrDefault(s => s.Id == cmd.StepId);
        if (step is null) return OnboardingErrors.StepNotFound();
        if (!step.IsSkippable) return OnboardingErrors.StepNotSkippable();

        UnitResult<Error> skip = onboarding.SkipStep(cmd.StepId);
        if (skip.IsFailure) return skip.Error;

        onboarding.AdvanceTo(flowResult.Value.Steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .Select(s => s.Id));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;
        return cmd.StepId;
    }
}
