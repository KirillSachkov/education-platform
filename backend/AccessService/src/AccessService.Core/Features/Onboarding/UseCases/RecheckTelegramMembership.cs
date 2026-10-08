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
using PlatformAuth.Middleware;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Contracts.HttpCommunication;

namespace AccessService.Core.Features.Onboarding.UseCases;

/// <summary>
///     User-triggered recheck of the TELEGRAM onboarding step (epic #397). Covers the
///     edge where the <c>chat_member.confirmed</c> event was missed (e.g. the user joined
///     the chat before the bot could publish, or the event was lost). Verifies membership
///     on-demand via <see cref="ITelegramBotServiceClient.CheckPlanMembershipAsync"/>; if
///     the user IS a member and their onboarding has a pending TELEGRAM step, completes it
///     and advances the cursor. Soft-degrade: TelegramBotService failure or
///     <c>status == "unknown"</c> → no-op, returns current state with 200 (never 500).
/// </summary>
public sealed class RecheckTelegramMembershipEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/onboarding/{planId:guid}/steps/telegram/recheck/",
                async Task<EndpointResult<RecheckTelegramMembershipResponse>> (
                    [FromRoute] Guid planId,
                    [FromServices] RecheckTelegramMembershipHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new RecheckTelegramMembershipCommand(planId), ct))
            .RequireAuthorization();
    }
}

public sealed record RecheckTelegramMembershipCommand(Guid PlanId) : ICommand;

public sealed class RecheckTelegramMembershipHandler
    : ICommandHandler<RecheckTelegramMembershipResponse, RecheckTelegramMembershipCommand>
{
    private const string STATUS_UNKNOWN = "unknown";

    private readonly IUserPlanOnboardingsRepository _onboardings;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITelegramBotServiceClient _telegram;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<RecheckTelegramMembershipHandler> _logger;

    public RecheckTelegramMembershipHandler(
        IUserPlanOnboardingsRepository onboardings,
        IPlanOnboardingFlowsRepository flows,
        ITelegramBotServiceClient telegram,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<RecheckTelegramMembershipHandler> logger)
    {
        _onboardings = onboardings;
        _flows = flows;
        _telegram = telegram;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<RecheckTelegramMembershipResponse, Error>> Handle(
        RecheckTelegramMembershipCommand cmd, CancellationToken ct)
    {
        UserPlanOnboarding? onboarding = await _onboardings.GetAsync(_user.UserId, cmd.PlanId, ct);
        if (onboarding is null) return OnboardingErrors.OnboardingNotFound();

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(
            f => f.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        PlanOnboardingFlow flow = flowResult.Value;
        PlanOnboardingStep? telegramStep = flow.Steps
            .FirstOrDefault(s => s.Type == PlanOnboardingStepType.TELEGRAM);

        // Нет TELEGRAM-шага в этом flow — проверять нечего.
        if (telegramStep is null)
            return new RecheckTelegramMembershipResponse(Completed: false, Status: STATUS_UNKNOWN);

        // Уже завершён → не дёргаем TBS (членство может легитимно измениться), но ПРОДВИГАЕМ
        // курсор: back-nav по степперу (return-to) / пропущенный advance мог оставить курсор на
        // завершённом TELEGRAM-шаге, и тогда «проверить» отвечал «подтверждено», а wizard стоял (#596).
        if (onboarding.CompletedStepIds.Contains(telegramStep.Id))
        {
            onboarding.AdvanceTo(flow.Steps
                .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
                .Select(s => s.Id));
            UnitResult<Error> advanceSave = await _transactions.SaveChangesAsync(ct);
            if (advanceSave.IsFailure) return advanceSave.Error;

            _logger.LogInformation(
                "TELEGRAM onboarding step already completed — stuck cursor advanced via recheck for user={UserId} plan={PlanId}",
                _user.UserId, cmd.PlanId);

            return new RecheckTelegramMembershipResponse(Completed: true, Status: "member");
        }

        Result<PlanMembershipDto, Error> membership =
            await _telegram.CheckPlanMembershipAsync(_user.UserId, cmd.PlanId, ct);

        // Soft-degrade: TelegramBotService unavailable → no-op, report unknown.
        if (membership.IsFailure)
        {
            _logger.LogWarning(
                "TelegramBotService.CheckPlanMembership failed for user={UserId} plan={PlanId}: {Error}. Soft-degrade — TELEGRAM step left pending.",
                _user.UserId, cmd.PlanId, membership.Error.GetMessage());
            return new RecheckTelegramMembershipResponse(Completed: false, Status: STATUS_UNKNOWN);
        }

        PlanMembershipDto dto = membership.Value;
        if (!dto.IsMember)
        {
            // not_member or unknown → leave step pending, surface the status verbatim.
            return new RecheckTelegramMembershipResponse(Completed: false, Status: dto.Status);
        }

        UnitResult<Error> completion = onboarding.CompleteStep(telegramStep.Id);
        if (completion.IsFailure) return completion.Error;

        onboarding.AdvanceTo(flow.Steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .Select(s => s.Id));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        _logger.LogInformation(
            "TELEGRAM onboarding step completed via recheck for user={UserId} plan={PlanId}",
            _user.UserId, cmd.PlanId);

        return new RecheckTelegramMembershipResponse(Completed: true, Status: dto.Status);
    }
}
