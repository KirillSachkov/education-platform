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

public sealed class ResetOnboardingEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/onboarding/{planId:guid}/reset/",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid planId,
                    [FromServices] ResetOnboardingHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new ResetOnboardingCommand(planId), ct))
            .RequireAuthorization();
    }
}

public sealed record ResetOnboardingCommand(Guid PlanId) : ICommand;

public sealed class ResetOnboardingHandler : ICommandHandler<Guid, ResetOnboardingCommand>
{
    private readonly IUserPlanOnboardingsRepository _onboardings;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly IPlanGrantsRepository _grants;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public ResetOnboardingHandler(
        IUserPlanOnboardingsRepository onboardings,
        IPlanOnboardingFlowsRepository flows,
        IPlanGrantsRepository grants,
        ITransactionManager transactions,
        UserScopedData user,
        TimeProvider time)
    {
        _onboardings = onboardings;
        _flows = flows;
        _grants = grants;
        _transactions = transactions;
        _user = user;
        _time = time;
    }

    public async Task<Result<Guid, Error>> Handle(ResetOnboardingCommand cmd, CancellationToken ct)
    {
        // Flow первым — после reset GetCurrent вернёт null если flow выключен
        // (handler фильтрует !IsEnabled), юзер увидит success-toast и редирект на
        // /onboarding/plans/{id} → OnboardingGate увидит null → выкинет на /. UX-провал.
        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(
            f => f.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        if (!flowResult.Value.IsEnabled || flowResult.Value.Steps.Count == 0)
        {
            return OnboardingErrors.FlowDisabled();
        }

        IReadOnlyList<Guid> orderedStepIds = flowResult.Value.Steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .Select(s => s.Id)
            .ToList();

        // Гард Steps.Count==0 выше делает FirstOrDefault безопасным; локалкой
        // оставляем явное соответствие "первый шаг" с UserPlanOnboarding.Reset,
        // который тоже работает через FirstOrDefault на orderedStepIds.
        Guid firstStepId = orderedStepIds[0];

        UserPlanOnboarding? onboarding = await _onboardings.GetAsync(_user.UserId, cmd.PlanId, ct);
        if (onboarding is null)
        {
            // Self-heal: legacy grant'ы (выданные до появления PlanGrantCreatedOnboardingHandler
            // и таблицы user_plan_onboardings 2026-05-06) или grant'ы выпущенные пока flow был
            // disabled — не имеют onboarding state. Создаём на лету при условии, что у юзера
            // есть active grant: иначе любой аутентифицированный сможет создавать строки на
            // чужие планы.
            bool hasActiveGrant = await _grants.ExistsAsync(
                g => g.UserId == _user.UserId
                     && g.PlanId == cmd.PlanId
                     && g.Status == PlanGrantStatus.ACTIVE,
                ct);
            if (!hasActiveGrant) return OnboardingErrors.OnboardingNotFound();

            onboarding = UserPlanOnboarding.Start(_user.UserId, cmd.PlanId, _time.GetUtcNow());
            onboarding.SetCurrentStep(firstStepId);
            await _onboardings.AddAsync(onboarding, ct);
        }
        else
        {
            onboarding.Reset(orderedStepIds, _time.GetUtcNow());
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        return cmd.PlanId;
    }
}
