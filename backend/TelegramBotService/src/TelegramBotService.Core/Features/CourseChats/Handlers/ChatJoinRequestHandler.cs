using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using SharedKernel;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Messaging;
using TelegramBotFlow.Core.Routing;
using TelegramBotService.Core.Common;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Options;
using TelegramBotService.Domain.Audit;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.CourseChats.Handlers;

/// <summary>
///     Логика обработки <c>chat_join_request</c>: проверяет, привязан ли юзер к платформе
///     и есть ли у него активный <see cref="PlanGrantDto"/> на один из bound планов чата.
/// </summary>
public sealed class ChatJoinRequestHandler
{

    private readonly IUserLinkRepository _userLinks;
    private readonly IChatBindingRepository _bindings;
    private readonly IAccessServiceClient _accessClient;
    private readonly IChatAdministrationApi _chatApi;
    private readonly IBotNotifier _notifier;
    private readonly PlanWelcomeService _planWelcome;
    private readonly IBotDecisionLogger _audit;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly TelegramNotificationOptions _options;
    private readonly ILogger<ChatJoinRequestHandler> _logger;

    public ChatJoinRequestHandler(
        IUserLinkRepository userLinks,
        IChatBindingRepository bindings,
        IAccessServiceClient accessClient,
        IChatAdministrationApi chatApi,
        IBotNotifier notifier,
        PlanWelcomeService planWelcome,
        IBotDecisionLogger audit,
        IOutboxService outbox,
        ITransactionManager transactions,
        IOptions<TelegramNotificationOptions> options,
        ILogger<ChatJoinRequestHandler> logger)
    {
        _userLinks = userLinks;
        _bindings = bindings;
        _accessClient = accessClient;
        _chatApi = chatApi;
        _notifier = notifier;
        _planWelcome = planWelcome;
        _audit = audit;
        _outbox = outbox;
        _transactions = transactions;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IEndpointResult> HandleAsync(UpdateContext ctx)
    {
        ChatJoinRequest? request = ctx.Update.ChatJoinRequest;
        if (request is null)
            return BotResults.Empty();

        long chatId = request.Chat.Id;
        long userId = request.From.Id;
        string? username = request.From.Username;

        // 1) Планы, привязанные к чату с enrollmentGrantsMembership=true.
        IReadOnlyList<ChatBinding> chatBindings = await _bindings.GetManyByAsync(
            x => x.TelegramChatId == chatId && x.EnrollmentGrantsMembership,
            ctx.CancellationToken);

        if (chatBindings.Count == 0)
        {
            await _chatApi.DeclineChatJoinRequestAsync(chatId, userId, ctx.CancellationToken);
            await _audit.LogAsync(chatId, userId, BotDecisions.JOIN_REQUEST_DECLINED_NO_BINDING,
                ct: ctx.CancellationToken);
            _logger.LogInformation(
                "Declined join request to chat {ChatId} for user {UserId}: chat is not bound to any plan",
                chatId, userId);
            return BotResults.Empty();
        }

        // 2) Платформенный аккаунт привязан?
        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.TelegramUserId == userId, ctx.CancellationToken);

        if (linkResult.IsFailure)
        {
            await _chatApi.DeclineChatJoinRequestAsync(chatId, userId, ctx.CancellationToken);
            await _audit.LogAsync(chatId, userId, BotDecisions.JOIN_REQUEST_DECLINED_NO_LINK,
                ct: ctx.CancellationToken);
            await DmLinkAccountAsync(userId, username, ctx.CancellationToken);
            _logger.LogInformation(
                "Declined join request to chat {ChatId} for user {UserId}: account not linked to platform",
                chatId, userId);
            return BotResults.Empty();
        }

        UserLink link = linkResult.Value;

        // 3) Есть ли активный plan-grant на любой из bound планов?
        Result<IReadOnlyList<PlanGrantDto>, Error> grantsResult =
            await _accessClient.GetUserGrantsAsync(link.PlatformUserId, ctx.CancellationToken);

        if (grantsResult.IsFailure)
        {
            await _chatApi.DeclineChatJoinRequestAsync(chatId, userId, ctx.CancellationToken);
            await _audit.LogAsync(chatId, userId, BotDecisions.JOIN_REQUEST_DECLINED_PROGRESS_ERROR,
                reason: grantsResult.Error.Messages[0].Code, ct: ctx.CancellationToken);
            await DmTemporaryFailureAsync(userId, ctx.CancellationToken);
            _logger.LogWarning(
                "Declined join request to chat {ChatId} for user {UserId}: AccessService check failed ({Code})",
                chatId, userId, grantsResult.Error.Messages[0].Code);
            return BotResults.Empty();
        }

        Result<IReadOnlyDictionary<Guid, PlanGrantDto>, Error> accessResult =
            await TelegramGrantChatAccessResolver.ResolveAsync(
                grantsResult.Value,
                _accessClient,
                ctx.CancellationToken);
        if (accessResult.IsFailure)
        {
            await _chatApi.DeclineChatJoinRequestAsync(chatId, userId, ctx.CancellationToken);
            await _audit.LogAsync(chatId, userId, BotDecisions.JOIN_REQUEST_DECLINED_PROGRESS_ERROR,
                reason: accessResult.Error.Messages[0].Code, ct: ctx.CancellationToken);
            await DmTemporaryFailureAsync(userId, ctx.CancellationToken);
            _logger.LogWarning(
                "Declined join request to chat {ChatId} for user {UserId}: Telegram access resolution failed ({Code})",
                chatId, userId, accessResult.Error.Messages[0].Code);
            return BotResults.Empty();
        }

        Guid[] boundPlanIds = chatBindings.Select(b => b.PlanId).Distinct().ToArray();
        Guid[] matchedPlanIds = boundPlanIds
            .Where(accessResult.Value.ContainsKey)
            .ToArray();

        if (matchedPlanIds.Length > 0)
        {
            ChatApiResult<bool> approveResult = await _chatApi.ApproveChatJoinRequestAsync(
                chatId,
                userId,
                ctx.CancellationToken);
            if (approveResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to approve join request to chat {ChatId} for user {UserId}: {Code} {Message}",
                    chatId,
                    userId,
                    approveResult.ErrorCode,
                    approveResult.ErrorMessage);
                return BotResults.Empty();
            }

            await _audit.LogAsync(chatId, userId, BotDecisions.JOIN_REQUEST_APPROVED,
                planId: matchedPlanIds[0], ct: ctx.CancellationToken);

            // Publish ChatMemberConfirmed для каждого matched плана — чат может быть bound
            // к нескольким планам, на каждый из которых у юзера активный grant. AccessService
            // consume'ит для подтверждения членства / онбординга.
            DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
            foreach (Guid planId in matchedPlanIds)
            {
                await _outbox.PublishAsync(
                    new ChatMemberConfirmed(link.PlatformUserId, planId, chatId, occurredAt));
            }

            // Flush outbox — без entity-write через ITransactionManager envelope'ы дропнулись бы
            // при dispose DbContext'а (см. docs/agents/wolverine-tests.md).
            UnitResult<Error> flush = await _transactions.SaveChangesAsync(ctx.CancellationToken);
            if (flush.IsFailure)
            {
                _logger.LogError(
                    "Failed to flush ChatMemberConfirmed outbox after approving chat {ChatId} for user {UserId}: {Code}",
                    chatId, userId, flush.Error.Messages[0].Code);
                throw flush.Error.AsTransient().ToException();
            }

            _logger.LogInformation(
                "Approved join request to chat {ChatId} for user {UserId} on {PlanCount} plan(s) {PlanIds}",
                chatId, userId, matchedPlanIds.Length,
                string.Join(",", matchedPlanIds.Select(id => id.ToString("N"))));

            // B (#411): пост приветствия плана прямо В ГРУППЕ при входе участника.
            // Best-effort, не влияет на approve. Постим один раз на join (первый matched
            // план с непустым welcome) — чтобы не спамить группу несколькими сообщениями.
            // Dedup per (user, plan) в PlanWelcomeService — повторный approve/grant/resync
            // того же пользователя не задублирует приветствие (#444).
            await PostPlanWelcomeInGroupAsync(
                chatId, link.TelegramUserId, matchedPlanIds, ctx.CancellationToken);

            return BotResults.Empty();
        }

        await _chatApi.DeclineChatJoinRequestAsync(chatId, userId, ctx.CancellationToken);
        await _audit.LogAsync(chatId, userId, BotDecisions.JOIN_REQUEST_DECLINED_NO_ENROLLMENT,
            ct: ctx.CancellationToken);
        await DmEnrollCtaAsync(userId, boundPlanIds, ctx.CancellationToken);
        _logger.LogInformation(
            "Declined join request to chat {ChatId} for user {UserId}: no active plan-grant matching {PlanCount} bound plans",
            chatId, userId, boundPlanIds.Length);

        return BotResults.Empty();
    }

    /// <summary>
    ///     Постит настроенное автором приветствие плана в группу при входе участника (#411).
    ///     Делегирует в <see cref="PlanWelcomeService"/> (dedup per (user, plan), best-effort).
    ///     Берёт первый из <paramref name="matchedPlanIds"/> план, у которого есть welcome.
    /// </summary>
    private async Task PostPlanWelcomeInGroupAsync(
        long chatId, long telegramUserId, Guid[] matchedPlanIds, CancellationToken ct)
    {
        foreach (Guid planId in matchedPlanIds)
        {
            WelcomeOutcome outcome = await _planWelcome.TrySendWelcomeAsync(
                planId, telegramUserId, chatId, WelcomeDestination.Group, ct, force: false);

            // Sent / AlreadySent / SendFailed — этот план «обработан», дальше не идём (один
            // welcome на join). NoWelcomeConfigured — у плана нет приветствия, пробуем следующий.
            if (outcome != WelcomeOutcome.NoWelcomeConfigured)
                return;
        }
    }

    private async Task DmLinkAccountAsync(long userId, string? username, CancellationToken ct)
    {
        try
        {
            string greeting = string.IsNullOrEmpty(username) ? "Привет!" : $"Привет, @{username}!";
            string text = $"{greeting}\n\nЧтобы попасть в чат, сначала привяжи аккаунт платформы. " +
                          "Зайди в свой профиль и нажми «Привязать Telegram».";

            string profileUrl = BuildAbsoluteUrl("/settings/integrations");
            InlineKeyboardMarkup? keyboard = UrlGuard.IsPublic(profileUrl)
                ? new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("Открыть профиль", profileUrl))
                : null;

            await _notifier.SendTextAsync(userId, text, keyboard, ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex,
                "DM to user {UserId} for link prompt failed (likely never started bot in DM)", userId);
        }
    }

    private async Task DmTemporaryFailureAsync(long userId, CancellationToken ct)
    {
        try
        {
            await _notifier.SendTextAsync(
                userId,
                "⏳ Не получилось проверить твой доступ прямо сейчас — система временно недоступна. " +
                "Попробуй открыть ссылку-приглашение в чат через несколько минут.",
                ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex,
                "DM to user {UserId} for temporary failure failed (likely never started bot in DM)", userId);
        }
    }

    private async Task DmEnrollCtaAsync(long userId, Guid[] planIds, CancellationToken ct)
    {
        try
        {
            string platformUrl = BuildAbsoluteUrl("/");
            string text = "У тебя нет активного доступа к плану, чат которого ты пытаешься открыть. " +
                          "Купи или активируй план на платформе и снова открой ссылку — бот пустит автоматически.";

            InlineKeyboardMarkup? keyboard = UrlGuard.IsPublic(platformUrl)
                ? new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("🌐 Открыть платформу", platformUrl))
                : null;

            _logger.LogInformation(
                "Sending plan-CTA to user {UserId} for {PlanCount} bound plan(s) {PlanIds}",
                userId, planIds.Length, string.Join(",", planIds.Select(id => id.ToString("N"))));

            await _notifier.SendTextAsync(userId, text, keyboard, ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex,
                "DM to user {UserId} for enroll CTA failed (likely never started bot in DM)", userId);
        }
    }

    private string BuildAbsoluteUrl(string relative)
    {
        string baseUrl = (_options.FrontendBaseUrl ?? string.Empty).TrimEnd('/');
        return baseUrl.Length == 0 ? relative : $"{baseUrl}{relative}";
    }
}
