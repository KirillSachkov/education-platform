using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Messaging.Consumers;

/// <summary>
///     F1: на <c>plan_grant.created</c>:
///     1) Если у плана есть привязанные чаты с <c>EnrollmentGrantsMembership=true</c>,
///        отправляет юзеру в личку invite link каждого такого чата.
///     2) Если юзер не привязал Telegram — skip (получит invite после линковки).
///     3) Cap-check <c>cap:COMMUNITY_ACCESS</c> для FREE LEARN_ONLY планов.
///     4) (#625) Если у trial FULL_ALL плана нет прямых bindings — использует bindings
///        единственного канонического lifetime-плана, выбранного AccessService.
/// </summary>
public sealed class PlanGrantCreatedTelegramHandler
{
    private const string CAP_COMMUNITY_ACCESS = "COMMUNITY_ACCESS";
    private const string TIER_FULL_ALL = "FULL_ALL";

    private readonly IUserLinkRepository _userLinks;
    private readonly IChatBindingRepository _bindings;
    private readonly IAccessServiceClient _accessClient;
    private readonly IBotNotifier _notifier;
    private readonly PlanWelcomeService _planWelcome;
    private readonly ChatMembershipChecker _membership;
    private readonly ILogger<PlanGrantCreatedTelegramHandler> _logger;

    public PlanGrantCreatedTelegramHandler(
        IUserLinkRepository userLinks,
        IChatBindingRepository bindings,
        IAccessServiceClient accessClient,
        IBotNotifier notifier,
        PlanWelcomeService planWelcome,
        ChatMembershipChecker membership,
        ILogger<PlanGrantCreatedTelegramHandler> logger)
    {
        _userLinks = userLinks;
        _bindings = bindings;
        _accessClient = accessClient;
        _notifier = notifier;
        _planWelcome = planWelcome;
        _membership = membership;
        _logger = logger;
    }

    public async Task Handle(PlanGrantCreated evt, CancellationToken cancellationToken)
    {
        Guid planId = evt.PlanId;
        IReadOnlyList<ChatBinding> bound = await _bindings.GetManyByAsync(
            x => x.PlanId == planId && x.EnrollmentGrantsMembership,
            cancellationToken);

        // #625 trial FULL_ALL fallback: trial plan is a separate Plan entity with its own PlanId
        // and typically no direct ChatBinding (bindings live on the lifetime FULL_ALL plan).
        // ExpiresAt != null identifies a time-bounded (trial) grant; PlanTier == FULL_ALL
        // confirms the same access scope. For such grants we find and use bindings of other
        // FULL_ALL plans so the invite DM is still sent.
        if (bound.Count == 0 &&
            evt.ExpiresAt.HasValue &&
            string.Equals(evt.PlanTier, TIER_FULL_ALL, StringComparison.Ordinal))
        {
            bound = await FindFullAllPeerBindingsAsync(planId, cancellationToken);
        }

        if (bound.Count == 0)
            return;

        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.PlatformUserId == evt.UserId, cancellationToken);
        if (linkResult.IsFailure)
        {
            _logger.LogDebug(
                "Plan-grant created for user {UserId} on plan {PlanId} but no Telegram link — skip DM",
                evt.UserId, planId);
            return;
        }

        UserLink link = linkResult.Value;

        // Soft-blocked link → skip DM. The bot was previously blocked by the user; sending now
        // would trigger 403 "bot was blocked", swallowed by the outer try/catch as a generic
        // warning and never updates the block state. Mirrors NotificationCreatedTelegramHandler.
        if (!link.IsActive)
        {
            _logger.LogDebug(
                "UserLink for {UserId} is soft-blocked; skip F1 invite DM for plan {PlanId}",
                evt.UserId, planId);
            return;
        }

        bool hasCommunityAccess = await HasCommunityAccessAsync(evt, cancellationToken);
        if (!hasCommunityAccess)
        {
            _logger.LogInformation(
                "User {UserId} got plan-grant {GrantId} без COMMUNITY_ACCESS — skip chat invite DM",
                evt.UserId, evt.GrantId);
            return;
        }

        foreach (ChatBinding binding in bound.DistinctBy(x => x.TelegramChatId))
        {
            MembershipStatus status = await _membership.GetStatusAsync(
                binding.TelegramChatId, link.TelegramUserId, cancellationToken);
            if (status == MembershipStatus.Member)
            {
                _logger.LogDebug(
                    "User {UserId} already member of chat {ChatId} — skip invite DM, ensure welcome",
                    link.TelegramUserId, binding.TelegramChatId);
                await _planWelcome.TrySendWelcomeAsync(
                    binding.PlanId, link.TelegramUserId, binding.TelegramChatId,
                    WelcomeDestination.DirectMessage, cancellationToken, force: false);
                continue;
            }

            try
            {
                string title = string.IsNullOrEmpty(binding.ChatTitle) ? "чат плана" : binding.ChatTitle;
                string text = $"Тебе открылся доступ к чату «{title}». " +
                              "Жми кнопку ниже, чтобы вступить — бот пустит автоматически.";

                InlineKeyboardMarkup keyboard = new(InlineKeyboardButton.WithUrl(
                    "Войти в чат", binding.InviteLink));

                await _notifier.SendTextAsync(link.TelegramUserId, text, keyboard, ct: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to DM invite link to user {UserId} chat {ChatId}",
                    link.TelegramUserId, binding.TelegramChatId);
            }
        }
    }

    /// <summary>
    ///     (#625) Для trial FULL_ALL гранта, у которого нет прямых ChatBinding'ов, ищет
    ///     биндинги единственного канонического lifetime-плана, выбранного AccessService
    ///     по тем же доменным правилам, что onboarding. Это исключает утечку чатов других
    ///     FULL_ALL-планов и убирает полный scan bindings с N+1 S2S-вызовами.
    /// </summary>
    private async Task<IReadOnlyList<ChatBinding>> FindFullAllPeerBindingsAsync(
        Guid trialPlanId,
        CancellationToken ct)
    {
        Result<PlanTelegramInfoDto, Error> info =
            await _accessClient.GetPlanTelegramInfoAsync(trialPlanId, ct);
        if (info.IsFailure)
            throw info.Error.AsTransient().ToException();

        Guid? canonicalPlanId = info.Value.CanonicalTelegramPlanId;
        if (canonicalPlanId is null || canonicalPlanId == trialPlanId)
            return [];

        IReadOnlyList<ChatBinding> result = await _bindings.GetManyByAsync(
            x => x.EnrollmentGrantsMembership && x.PlanId == canonicalPlanId.Value,
            ct);

        if (result.Count > 0)
        {
            _logger.LogInformation(
                "Trial FULL_ALL grant fallback (#625): no direct bindings for plan {TrialPlanId}, " +
                "using {Count} binding(s) from FULL_ALL peer plans",
                trialPlanId, result.Count);
        }

        return result;
    }

    private async Task<bool> HasCommunityAccessAsync(
        PlanGrantCreated evt,
        CancellationToken cancellationToken)
    {
        if (evt.Capabilities is not null)
            return evt.Capabilities.Contains(CAP_COMMUNITY_ACCESS, StringComparer.Ordinal);

        Result<PlanTelegramInfoDto, Error> info =
            await _accessClient.GetPlanTelegramInfoAsync(evt.PlanId, cancellationToken);
        if (info.IsFailure)
            throw info.Error.AsTransient().ToException();

        return info.Value.Capabilities?.Contains(CAP_COMMUNITY_ACCESS, StringComparer.Ordinal) == true;
    }
}
