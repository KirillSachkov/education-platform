using AccessService.Contracts.Onboarding;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Onboarding.UseCases;

public sealed class GetOnboardingFlowEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/plans/{planId:guid}/onboarding-flow/", async Task<EndpointResult<OnboardingFlowResponse>> (
                [FromRoute] Guid planId,
                [FromServices] GetOnboardingFlowHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetOnboardingFlowQuery(planId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record GetOnboardingFlowQuery(Guid PlanId) : ICommand;

public sealed class GetOnboardingFlowHandler : ICommandHandler<OnboardingFlowResponse, GetOnboardingFlowQuery>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public GetOnboardingFlowHandler(
        IPlansRepository plans,
        IPlanOnboardingFlowsRepository flows,
        UserScopedData user,
        TimeProvider time)
    {
        _plans = plans;
        _flows = flows;
        _user = user;
        _time = time;
    }

    public async Task<Result<OnboardingFlowResponse, Error>> Handle(GetOnboardingFlowQuery q, CancellationToken ct)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == q.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        if (!_user.IsOwnerOrAdmin(planResult.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(f => f.PlanId == q.PlanId, ct);
        if (flowResult.IsSuccess)
        {
            return MapToResponse(flowResult.Value);
        }

        // Эфемерный empty-flow: GET идемпотентен и не пишет в БД. Реальная строка
        // создаётся первым PUT/POST (Enable / AddMarkdownStep) — see SetEnabled / AddMarkdownStep.
        return new OnboardingFlowResponse(
            q.PlanId,
            IsEnabled: false,
            CreatedAt: _time.GetUtcNow(),
            UpdatedAt: _time.GetUtcNow(),
            Steps: []);
    }

    internal static OnboardingFlowResponse MapToResponse(PlanOnboardingFlow flow) =>
        new(
            flow.PlanId,
            flow.IsEnabled,
            flow.CreatedAt,
            flow.UpdatedAt,
            flow.Steps
                .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
                .Select(s => new OnboardingStepDto(
                    s.Id,
                    s.Type.ToString(),
                    s.IsSkippable,
                    s.SortOrder.Value,
                    s.Title,
                    s.Body))
                .ToList());
}
