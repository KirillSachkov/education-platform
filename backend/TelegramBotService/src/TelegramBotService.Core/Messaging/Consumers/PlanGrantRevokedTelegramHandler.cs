using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.Audit;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Messaging.Consumers;

/// <summary>
///     F6: на <c>plan_grant.revoked</c> и <c>plan_grant.expired</c> — для bound chat'ов
///     плана с <c>auto_kick_on_revoke=true</c> выкидываем юзера из чата (ban + unban —
///     kick без permaban). По дефолту флаг выкл, включается per-binding через PATCH.
/// </summary>
public sealed class PlanGrantRevokedTelegramHandler
{
    private readonly IUserLinkRepository _userLinks;
    private readonly IChatBindingRepository _bindings;
    private readonly IAccessServiceClient _accessClient;
    private readonly IChatAdministrationApi _chatApi;
    private readonly ChatMembershipChecker _membership;
    private readonly IBotDecisionLogger _audit;
    private readonly ILogger<PlanGrantRevokedTelegramHandler> _logger;

    public PlanGrantRevokedTelegramHandler(
        IUserLinkRepository userLinks,
        IChatBindingRepository bindings,
        IAccessServiceClient accessClient,
        IChatAdministrationApi chatApi,
        ChatMembershipChecker membership,
        IBotDecisionLogger audit,
        ILogger<PlanGrantRevokedTelegramHandler> logger)
    {
        _userLinks = userLinks;
        _bindings = bindings;
        _accessClient = accessClient;
        _chatApi = chatApi;
        _membership = membership;
        _audit = audit;
        _logger = logger;
    }

    public Task Handle(PlanGrantRevoked evt, CancellationToken ct) =>
        ProcessAsync(evt.UserId, evt.PlanId, evt.CanonicalTelegramPlanId, ct);

    public Task Handle(PlanGrantExpired evt, CancellationToken ct) =>
        ProcessAsync(evt.UserId, evt.PlanId, evt.CanonicalTelegramPlanId, ct);

    public Task Handle(PlanGrantRenewalRefunded evt, CancellationToken ct) =>
        ProcessAsync(evt.UserId, evt.PlanId, evt.CanonicalTelegramPlanId, ct);

    private async Task ProcessAsync(
        Guid userId,
        Guid planId,
        Guid? eventBindingPlanId,
        CancellationToken cancellationToken)
    {
        Guid bindingPlanId = eventBindingPlanId ?? planId;
        if (eventBindingPlanId is null)
        {
            Result<PlanTelegramInfoDto, Error> endedPlanInfo =
                await _accessClient.GetPlanTelegramInfoAsync(planId, cancellationToken);
            if (endedPlanInfo.IsSuccess)
            {
                bindingPlanId = endedPlanInfo.Value.CanonicalTelegramPlanId ?? planId;
            }
            else if (endedPlanInfo.Error.Type != ErrorType.NOT_FOUND)
            {
                throw endedPlanInfo.Error.AsTransient().ToException();
            }
            else
            {
                _logger.LogWarning(
                    "Ended plan {PlanId} no longer exists; falling back to direct Telegram binding cleanup",
                    planId);
            }
        }
        IReadOnlyList<ChatBinding> bound = await _bindings.GetManyByAsync(
            x => x.PlanId == bindingPlanId && x.AutoKickOnRevoke, cancellationToken);

        if (bound.Count == 0)
            return;

        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.PlatformUserId == userId, cancellationToken);

        if (linkResult.IsFailure)
        {
            _logger.LogDebug(
                "Plan-grant revoked but no Telegram link for user {UserId} — nothing to kick",
                userId);
            return;
        }

        long telegramUserId = linkResult.Value.TelegramUserId;
        Result<IReadOnlyList<PlanGrantDto>, Error> grantsResult =
            await _accessClient.GetUserGrantsAsync(userId, cancellationToken);
        if (grantsResult.IsFailure)
            throw grantsResult.Error.AsTransient().ToException();

        Result<IReadOnlyDictionary<Guid, PlanGrantDto>, Error> accessResult =
            await TelegramGrantChatAccessResolver.ResolveAsync(
                grantsResult.Value,
                _accessClient,
                cancellationToken);
        if (accessResult.IsFailure)
            throw accessResult.Error.AsTransient().ToException();

        long[] targetChatIds = bound.Select(binding => binding.TelegramChatId).Distinct().ToArray();
        IReadOnlyList<ChatBinding> allTargetChatBindings = await _bindings.GetManyByAsync(
            binding => targetChatIds.Contains(binding.TelegramChatId)
                       && binding.EnrollmentGrantsMembership,
            cancellationToken);

        foreach (ChatBinding binding in bound)
        {
            bool stillAuthorized = allTargetChatBindings.Any(candidate =>
                candidate.TelegramChatId == binding.TelegramChatId
                && accessResult.Value.ContainsKey(candidate.PlanId));
            if (stillAuthorized)
            {
                _logger.LogDebug(
                    "Skip auto-kick: user {UserId} retains access to chat {ChatId} through another active plan",
                    telegramUserId,
                    binding.TelegramChatId);
                continue;
            }

            MembershipStatus status = await _membership.GetStatusAsync(
                binding.TelegramChatId, telegramUserId, cancellationToken);
            if (status == MembershipStatus.NotMember)
            {
                _logger.LogDebug(
                    "Skip auto-kick: user {UserId} confirmed not in chat {ChatId}",
                    telegramUserId, binding.TelegramChatId);
                continue;
            }

            ChatApiResult<bool> kickResult = await _chatApi.KickChatMemberAsync(
                binding.TelegramChatId, telegramUserId, cancellationToken);

            if (kickResult.IsFailure)
            {
                if (kickResult.ErrorCode is ChatApiErrorCode.RateLimited
                    or ChatApiErrorCode.ServiceUnavailable)
                {
                    throw Error.Failure(
                            "telegram.transient_api_error",
                            kickResult.ErrorMessage ?? "Telegram temporarily failed while removing a member")
                        .AsTransient()
                        .ToException();
                }

                _logger.LogWarning(
                    "Auto-kick failed. ChatId={ChatId} UserId={UserId} Code={Code}",
                    binding.TelegramChatId, telegramUserId, kickResult.ErrorCode);
            }
            else
            {
                await _audit.LogAsync(binding.TelegramChatId, telegramUserId,
                    BotDecisions.AUTO_KICK_ENROLLMENT_REVOKED,
                    planId: planId, ct: cancellationToken);
                _logger.LogInformation(
                    "Auto-kicked user {UserId} from chat {ChatId} after plan-grant revoke (plan {PlanId})",
                    telegramUserId, binding.TelegramChatId, planId);
            }
        }
    }
}
