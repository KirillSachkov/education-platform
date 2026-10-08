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
///     Author/admin bulk reset of onboarding for ALL grant-holders of a plan (epic #397).
///     Re-runs the wizard for everyone — used after the author meaningfully changes the
///     onboarding flow and wants existing students to go through it again. Each row is
///     reset to the first step (completed/skipped cleared, CompletedAt nulled).
///
///     Resets are committed in chunks via <see cref="ITransactionManager"/> so a plan with
///     thousands of grant-holders doesn't hold one giant transaction. If the flow is
///     disabled or has no steps, returns <c>ResetCount = 0</c> without touching any row
///     (resetting to a non-existent first step would strand users in a gate-loop).
/// </summary>
public sealed class ResetAllOnboardingsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/{planId:guid}/onboarding-flow/reset-all/",
                async Task<EndpointResult<ResetAllOnboardingsResponse>> (
                    [FromRoute] Guid planId,
                    [FromServices] ResetAllOnboardingsHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new ResetAllOnboardingsCommand(planId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record ResetAllOnboardingsCommand(Guid PlanId) : ICommand;

public sealed class ResetAllOnboardingsHandler
    : ICommandHandler<ResetAllOnboardingsResponse, ResetAllOnboardingsCommand>
{
    private const int CHUNK_SIZE = 200;

    private readonly IPlansRepository _plans;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly IUserPlanOnboardingsRepository _onboardings;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;
    private readonly ILogger<ResetAllOnboardingsHandler> _logger;

    public ResetAllOnboardingsHandler(
        IPlansRepository plans,
        IPlanOnboardingFlowsRepository flows,
        IUserPlanOnboardingsRepository onboardings,
        ITransactionManager transactions,
        UserScopedData user,
        TimeProvider time,
        ILogger<ResetAllOnboardingsHandler> logger)
    {
        _plans = plans;
        _flows = flows;
        _onboardings = onboardings;
        _transactions = transactions;
        _user = user;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<ResetAllOnboardingsResponse, Error>> Handle(
        ResetAllOnboardingsCommand cmd, CancellationToken ct)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == cmd.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        if (!_user.IsOwnerOrAdmin(planResult.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(
            f => f.PlanId == cmd.PlanId, ct);

        // No flow row / disabled / no steps → nothing to reset to. Don't crash; report 0.
        if (flowResult.IsFailure
            || !flowResult.Value.IsEnabled
            || flowResult.Value.Steps.Count == 0)
        {
            return new ResetAllOnboardingsResponse(ResetCount: 0);
        }

        IReadOnlyList<Guid> orderedStepIds = flowResult.Value.Steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .Select(s => s.Id)
            .ToList();

        DateTimeOffset now = _time.GetUtcNow();
        IReadOnlyList<UserPlanOnboarding> all = await _onboardings.GetManyByAsync(
            o => o.PlanId == cmd.PlanId, ct);

        int resetCount = 0;
        foreach (UserPlanOnboarding[] chunk in all.Chunk(CHUNK_SIZE))
        {
            foreach (UserPlanOnboarding onboarding in chunk)
            {
                onboarding.Reset(orderedStepIds, now);
            }

            UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
            if (save.IsFailure) return save.Error;
            resetCount += chunk.Length;
        }

        _logger.LogInformation(
            "Bulk-reset {Count} onboarding(s) for plan={PlanId} by user={UserId}",
            resetCount, cmd.PlanId, _user.UserId);

        return new ResetAllOnboardingsResponse(resetCount);
    }
}
