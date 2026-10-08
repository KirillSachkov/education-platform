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

public sealed class CompleteOnboardingEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/onboarding/{planId:guid}/complete/",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid planId,
                    [FromServices] CompleteOnboardingHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new CompleteOnboardingCommand(planId), ct))
            .RequireAuthorization();
    }
}

public sealed record CompleteOnboardingCommand(Guid PlanId) : ICommand;

public sealed class CompleteOnboardingHandler : ICommandHandler<Guid, CompleteOnboardingCommand>
{
    private readonly IUserPlanOnboardingsRepository _onboardings;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;
    private readonly ILogger<CompleteOnboardingHandler> _logger;

    public CompleteOnboardingHandler(
        IUserPlanOnboardingsRepository onboardings,
        IPlanOnboardingFlowsRepository flows,
        ITransactionManager transactions,
        UserScopedData user,
        TimeProvider time,
        ILogger<CompleteOnboardingHandler> logger)
    {
        _onboardings = onboardings;
        _flows = flows;
        _transactions = transactions;
        _user = user;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(CompleteOnboardingCommand cmd, CancellationToken ct)
    {
        UserPlanOnboarding? onboarding = await _onboardings.GetAsync(_user.UserId, cmd.PlanId, ct);
        if (onboarding is null) return OnboardingErrors.OnboardingNotFound();

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(
            f => f.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        IReadOnlyList<Guid> allStepIds = flowResult.Value.Steps.Select(s => s.Id).ToList();
        UnitResult<Error> complete = onboarding.Complete(allStepIds, _time.GetUtcNow());
        if (complete.IsFailure) return complete.Error;

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        _logger.LogInformation(
            "Onboarding completed: user={UserId} plan={PlanId}", _user.UserId, cmd.PlanId);

        return cmd.PlanId;
    }
}
