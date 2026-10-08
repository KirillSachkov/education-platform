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
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Contracts.HttpCommunication;

namespace AccessService.Core.Features.Admin;

/// <summary>
/// <c>POST /access/admin/users/{userId}/plans/{planId}/telegram/recheck/</c> (#444) —
/// admin/support variant of the user-facing
/// <see cref="Onboarding.UseCases.RecheckTelegramMembershipHandler"/>: re-checks the
/// TELEGRAM onboarding step for an ARBITRARY user (id from the route, not the caller).
/// If the user IS a member and their onboarding has a pending TELEGRAM step, completes
/// it and advances the cursor. Soft-degrade: TelegramBotService failure or
/// <c>status == "unknown"</c> → no-op, returns current state with 200 (never 500).
/// Used by support to push a "купил, но не попал в Telegram" user past the gate without
/// asking them to click recheck themselves.
/// </summary>
public sealed class RecheckTelegramMembershipForUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/admin/users/{userId:guid}/plans/{planId:guid}/telegram/recheck/",
                async Task<EndpointResult<RecheckTelegramMembershipResponse>> (
                    [FromRoute] Guid userId,
                    [FromRoute] Guid planId,
                    [FromServices] RecheckTelegramMembershipForUserHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new RecheckTelegramMembershipForUserCommand(userId, planId), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
    }
}

public sealed record RecheckTelegramMembershipForUserCommand(Guid UserId, Guid PlanId) : ICommand;

public sealed class RecheckTelegramMembershipForUserHandler
    : ICommandHandler<RecheckTelegramMembershipResponse, RecheckTelegramMembershipForUserCommand>
{
    private const string STATUS_UNKNOWN = "unknown";

    private readonly IUserPlanOnboardingsRepository _onboardings;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITelegramBotServiceClient _telegram;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<RecheckTelegramMembershipForUserHandler> _logger;

    public RecheckTelegramMembershipForUserHandler(
        IUserPlanOnboardingsRepository onboardings,
        IPlanOnboardingFlowsRepository flows,
        ITelegramBotServiceClient telegram,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<RecheckTelegramMembershipForUserHandler> logger)
    {
        _onboardings = onboardings;
        _flows = flows;
        _telegram = telegram;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<RecheckTelegramMembershipResponse, Error>> Handle(
        RecheckTelegramMembershipForUserCommand cmd, CancellationToken ct)
    {
        UserPlanOnboarding? onboarding = await _onboardings.GetAsync(cmd.UserId, cmd.PlanId, ct);
        if (onboarding is null) return OnboardingErrors.OnboardingNotFound();

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(
            f => f.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        PlanOnboardingFlow flow = flowResult.Value;
        PlanOnboardingStep? telegramStep = flow.Steps
            .FirstOrDefault(s => s.Type == PlanOnboardingStepType.TELEGRAM);

        // Нет TELEGRAM-шага в этом flow — проверять нечего.
        if (telegramStep is null)
        {
            LogAction(cmd, STATUS_UNKNOWN, completed: false);
            return new RecheckTelegramMembershipResponse(Completed: false, Status: STATUS_UNKNOWN);
        }

        // Уже завершён → не дёргаем TBS, но ПРОДВИГАЕМ курсор (см. #596): саппорт тоже должен
        // расцеплять юзера, застрявшего курсором на завершённом TELEGRAM-шаге (back-nav / return-to).
        if (onboarding.CompletedStepIds.Contains(telegramStep.Id))
        {
            onboarding.AdvanceTo(flow.Steps
                .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
                .Select(s => s.Id));
            UnitResult<Error> advanceSave = await _transactions.SaveChangesAsync(ct);
            if (advanceSave.IsFailure) return advanceSave.Error;
            LogAction(cmd, "member", completed: true);
            return new RecheckTelegramMembershipResponse(Completed: true, Status: "member");
        }

        Result<PlanMembershipDto, Error> membership =
            await _telegram.CheckPlanMembershipAsync(cmd.UserId, cmd.PlanId, ct);

        // Soft-degrade: TelegramBotService unavailable → no-op, report unknown.
        if (membership.IsFailure)
        {
            _logger.LogWarning(
                "TelegramBotService.CheckPlanMembership failed for user={UserId} plan={PlanId}: {Error}. Soft-degrade — TELEGRAM step left pending.",
                cmd.UserId, cmd.PlanId, membership.Error.GetMessage());
            LogAction(cmd, STATUS_UNKNOWN, completed: false);
            return new RecheckTelegramMembershipResponse(Completed: false, Status: STATUS_UNKNOWN);
        }

        PlanMembershipDto dto = membership.Value;
        if (!dto.IsMember)
        {
            // not_member or unknown → leave step pending, surface the status verbatim.
            LogAction(cmd, dto.Status, completed: false);
            return new RecheckTelegramMembershipResponse(Completed: false, Status: dto.Status);
        }

        UnitResult<Error> completion = onboarding.CompleteStep(telegramStep.Id);
        if (completion.IsFailure) return completion.Error;

        onboarding.AdvanceTo(flow.Steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .Select(s => s.Id));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        LogAction(cmd, dto.Status, completed: true);
        return new RecheckTelegramMembershipResponse(Completed: true, Status: dto.Status);
    }

    private void LogAction(RecheckTelegramMembershipForUserCommand cmd, string outcome, bool completed) =>
        _logger.LogInformation(
            "support-action {Action} admin={AdminUserId} target={TargetUserId} plan={PlanId} outcome={Outcome} completed={Completed}",
            "telegram-recheck", _user.UserId, cmd.UserId, cmd.PlanId, outcome, completed);
}
