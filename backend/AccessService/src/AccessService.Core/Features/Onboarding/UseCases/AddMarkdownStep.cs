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

public sealed class AddMarkdownStepEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/{planId:guid}/onboarding-flow/steps/", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromBody] AddMarkdownStepRequest request,
                [FromServices] AddMarkdownStepHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new AddMarkdownStepCommand(planId, request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record AddMarkdownStepCommand(Guid PlanId, AddMarkdownStepRequest Request) : ICommand;

public sealed class AddMarkdownStepHandler : ICommandHandler<Guid, AddMarkdownStepCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public AddMarkdownStepHandler(
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

    public async Task<Result<Guid, Error>> Handle(AddMarkdownStepCommand cmd, CancellationToken ct)
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

        Result<PlanOnboardingStep, Error> add = flow.AddMarkdownStep(
            cmd.Request.Title, cmd.Request.Body, cmd.Request.IsSkippable, now);
        if (add.IsFailure) return add.Error;

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;
        return add.Value.Id;
    }
}
