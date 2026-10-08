using AccessService.Contracts.Onboarding;
using AccessService.Core.Database;
using Core.Database;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Onboarding.UseCases;

public sealed class GetCurrentOnboardingEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/onboarding/current/", async Task<EndpointResult<CurrentOnboardingResponse?>> (
                [Microsoft.AspNetCore.Mvc.FromServices] GetCurrentOnboardingHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetCurrentOnboardingQuery(), ct))
            .RequireAuthorization();
    }
}

public sealed record GetCurrentOnboardingQuery() : ICommand;

public sealed class GetCurrentOnboardingHandler : ICommandHandler<CurrentOnboardingResponse?, GetCurrentOnboardingQuery>
{
    private readonly IUserPlanOnboardingsRepository _onboardings;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<GetCurrentOnboardingHandler> _logger;

    public GetCurrentOnboardingHandler(
        IUserPlanOnboardingsRepository onboardings,
        IPlanOnboardingFlowsRepository flows,
        IPlansRepository plans,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<GetCurrentOnboardingHandler> logger)
    {
        _onboardings = onboardings;
        _flows = flows;
        _plans = plans;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<CurrentOnboardingResponse?, Error>> Handle(
        GetCurrentOnboardingQuery query, CancellationToken ct)
    {
        IReadOnlyList<UserPlanOnboarding> pending = await _onboardings.GetManyByAsync(
            o => o.UserId == _user.UserId && o.CompletedAt == null, ct);

        if (pending.Count == 0)
        {
            return (CurrentOnboardingResponse?)null;
        }

        // Берём самый старый pending — последовательная очередь.
        UserPlanOnboarding oldest = pending.OrderBy(o => o.StartedAt).First();

        // Flow первым — early-exit если выключен (избегаем лишний plan lookup).
        // DbContext не thread-safe, поэтому параллелить эти запросы нельзя.
        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(
            f => f.PlanId == oldest.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        // Если автор после старта онбординга выключил его — не показываем wizard,
        // чтобы юзер не залип в gate-петле. Pending state остаётся в БД до того,
        // как flow снова включат либо grant отзовут.
        if (!flowResult.Value.IsEnabled)
        {
            return (CurrentOnboardingResponse?)null;
        }

        Result<Plan, Error> planResult = await _plans.GetByAsync(
            p => p.Id == oldest.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;

        // #497: self-heal курсора. Шаги flow меняются ПОСЛЕ старта онбординга
        // (bind/unbind TG-чата, toggle review-app, MARKDOWN-правки автора) — курсор
        // может стать null (юзер дошёл до «старого конца», а шаг добавили позже) или
        // указывать на удалённый шаг. Оба состояния рендерили CompletionView при
        // живых pending-шагах, а POST /complete/ корректно бил 409 has.pending.steps —
        // юзер попадал в тупик. Пере-наводим курсор на первый pending шаг.
        List<Guid> orderedStepIds = flowResult.Value.Steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .Select(s => s.Id)
            .ToList();
        bool cursorValid = oldest.CurrentStepId is not null
            && orderedStepIds.Contains(oldest.CurrentStepId.Value);
        bool hasPendingSteps = orderedStepIds.Exists(id =>
            !oldest.CompletedStepIds.Contains(id) && !oldest.SkippedStepIds.Contains(id));
        if (!cursorValid && hasPendingSteps)
        {
            oldest.AdvanceTo(orderedStepIds);
            UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
            if (save.IsFailure) return save.Error;

            _logger.LogInformation(
                "Onboarding cursor healed: user={UserId} plan={PlanId} cursor={CurrentStepId}",
                oldest.UserId, oldest.PlanId, oldest.CurrentStepId);
        }

        IReadOnlyList<OnboardingStepDto> steps = flowResult.Value.Steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .Select(s => new OnboardingStepDto(
                s.Id, s.Type.ToString(), s.IsSkippable, s.SortOrder.Value, s.Title, s.Body))
            .ToList();

        UserOnboardingStateDto state = new(
            oldest.StartedAt,
            oldest.CompletedAt,
            oldest.CurrentStepId,
            oldest.SkippedStepIds,
            oldest.CompletedStepIds);

        return new CurrentOnboardingResponse(
            planResult.Value.Id,
            planResult.Value.DisplayName.Value,
            planResult.Value.AuthorId,
            planResult.Value.GitHubOrg,
            steps,
            state);
    }
}
