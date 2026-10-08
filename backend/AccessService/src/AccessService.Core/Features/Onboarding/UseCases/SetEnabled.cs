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
using TelegramBotService.Contracts.HttpCommunication;

namespace AccessService.Core.Features.Onboarding.UseCases;

public sealed class SetOnboardingEnabledEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/access/plans/{planId:guid}/onboarding-flow/", async Task<EndpointResult<bool>> (
                [FromRoute] Guid planId,
                [FromBody] SetOnboardingEnabledRequest request,
                [FromServices] SetOnboardingEnabledHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new SetOnboardingEnabledCommand(planId, request.IsEnabled), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record SetOnboardingEnabledCommand(Guid PlanId, bool IsEnabled) : ICommand;

public sealed class SetOnboardingEnabledHandler : ICommandHandler<bool, SetOnboardingEnabledCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITelegramBotServiceClient _telegram;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;
    private readonly ILogger<SetOnboardingEnabledHandler> _logger;

    public SetOnboardingEnabledHandler(
        IPlansRepository plans,
        IPlanOnboardingFlowsRepository flows,
        ITelegramBotServiceClient telegram,
        ITransactionManager transactions,
        UserScopedData user,
        TimeProvider time,
        ILogger<SetOnboardingEnabledHandler> logger)
    {
        _plans = plans;
        _flows = flows;
        _telegram = telegram;
        _transactions = transactions;
        _user = user;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<bool, Error>> Handle(SetOnboardingEnabledCommand cmd, CancellationToken ct)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == cmd.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        Plan plan = planResult.Value;
        if (!_user.IsOwnerOrAdmin(plan.AuthorId))
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
            flow.Enable(now);
            // При первом включении гарантируем NOTIFICATIONS-шаг (всегда есть в активном flow).
            flow.EnsureAutoStep(PlanOnboardingStepType.NOTIFICATIONS, now);

            // Sync auto-steps по факту наличия интеграций у плана.
            if (!string.IsNullOrEmpty(plan.GitHubOrg))
            {
                flow.EnsureAutoStep(PlanOnboardingStepType.GITHUB, now);
            }

            // TG-шаг — закрываем gap: до этого шаг ensure'ился ТОЛЬКО на event
            // chat_binding.bound_to_plan, поэтому если автор привязал чат раньше
            // включения flow — событие уходило в early-exit (!IsEnabled) и шаг
            // не появлялся. Теперь на enable дёргаем TelegramBotService.
            // Soft-degrade: TG service down → не падаем, шаг ensure'ится при
            // следующем bind/unbind событии.
            Result<bool, Error> tgResult = await _telegram.HasActiveChatBindingAsync(plan.Id, ct);
            if (tgResult.IsSuccess && tgResult.Value)
            {
                flow.EnsureAutoStep(PlanOnboardingStepType.TELEGRAM, now);
            }
            else if (tgResult.IsFailure)
            {
                _logger.LogWarning(
                    "TelegramBotService.HasActiveChatBinding failed for plan={PlanId}: {Error}. Skipping TELEGRAM step ensure on enable; will sync via chat_binding events.",
                    plan.Id, tgResult.Error.GetMessage());
            }
        }
        else
        {
            flow.Disable(now);
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        _logger.LogInformation("Onboarding flow plan={PlanId} enabled={IsEnabled} by user={UserId}",
            plan.Id, cmd.IsEnabled, _user.UserId);

        return cmd.IsEnabled;
    }
}
