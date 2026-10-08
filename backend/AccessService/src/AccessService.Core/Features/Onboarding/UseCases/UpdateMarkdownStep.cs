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

public sealed class UpdateMarkdownStepEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/access/plans/{planId:guid}/onboarding-flow/steps/{stepId:guid}/", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromRoute] Guid stepId,
                [FromBody] UpdateMarkdownStepRequest request,
                [FromServices] UpdateMarkdownStepHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new UpdateMarkdownStepCommand(planId, stepId, request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record UpdateMarkdownStepCommand(
    Guid PlanId, Guid StepId, UpdateMarkdownStepRequest Request) : ICommand;

public sealed class UpdateMarkdownStepHandler : ICommandHandler<Guid, UpdateMarkdownStepCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public UpdateMarkdownStepHandler(
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

    public async Task<Result<Guid, Error>> Handle(UpdateMarkdownStepCommand cmd, CancellationToken ct)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == cmd.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        if (!_user.IsOwnerOrAdmin(planResult.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(f => f.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        UnitResult<Error> update = flowResult.Value.UpdateMarkdownStep(
            cmd.StepId, cmd.Request.Title, cmd.Request.Body, cmd.Request.IsSkippable, _time.GetUtcNow());
        if (update.IsFailure) return update.Error;

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;
        return cmd.StepId;
    }
}
