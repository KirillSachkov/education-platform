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

/// <summary>
///     Author-controlled toggle for the GITHUB_REVIEW_APP onboarding step (#307).
///     Unlike TELEGRAM / GITHUB org-invite, this step doesn't sync to a plan-level
///     setting — the author explicitly opts in via this endpoint. Idempotent: posting
///     the same value twice is a no-op.
/// </summary>
public sealed class ToggleGithubReviewAppStepEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/{planId:guid}/onboarding-flow/steps/github-review-app/",
                async Task<EndpointResult<bool>> (
                    [FromRoute] Guid planId,
                    [FromBody] ToggleGithubReviewAppStepRequest request,
                    [FromServices] ToggleGithubReviewAppStepHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new ToggleGithubReviewAppStepCommand(planId, request.IsEnabled), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record ToggleGithubReviewAppStepCommand(Guid PlanId, bool IsEnabled) : ICommand;

public sealed class ToggleGithubReviewAppStepHandler
    : ICommandHandler<bool, ToggleGithubReviewAppStepCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public ToggleGithubReviewAppStepHandler(
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

    public async Task<Result<bool, Error>> Handle(ToggleGithubReviewAppStepCommand cmd, CancellationToken ct)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == cmd.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        if (!_user.IsOwnerOrAdmin(planResult.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        DateTimeOffset now = _time.GetUtcNow();
        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(f => f.PlanId == cmd.PlanId, ct);
        PlanOnboardingFlow flow;
        if (flowResult.IsFailure)
        {
            flow = PlanOnboardingFlow.Create(cmd.PlanId, now);
            await _flows.AddAsync(flow, ct);
        }
        else
        {
            flow = flowResult.Value;
        }

        if (cmd.IsEnabled)
        {
            flow.EnsureAutoStep(PlanOnboardingStepType.GITHUB_REVIEW_APP, now);
        }
        else
        {
            flow.RemoveAutoStep(PlanOnboardingStepType.GITHUB_REVIEW_APP, now);
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;
        return cmd.IsEnabled;
    }
}
