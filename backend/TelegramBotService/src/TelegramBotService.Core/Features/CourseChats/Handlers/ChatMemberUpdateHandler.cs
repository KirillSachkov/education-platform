using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using SharedKernel;
using Telegram.Bot.Types;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Messaging;
using TelegramBotFlow.Core.Routing;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.CourseChats.Handlers;

/// <summary>
///     Реактив на <c>chat_member</c> update в bound chat — юзер вступил/был добавлен админом,
///     перешёл по старому invite link, или вступил через одобрение админа / авто-аппрув Telegram
///     (системное «X принят(а) в группу»). Этот путь НЕ проходит через
///     <see cref="ChatJoinRequestHandler"/> (бот сам ничего не аппрувит), поэтому до ST-1 (#616)
///     приветствие плана и event <see cref="ChatMemberConfirmed"/> вообще не выпускались.
///
///     Делает две независимые вещи на genuine NEW join:
///     <list type="number">
///         <item>
///             <b>Welcome + <see cref="ChatMemberConfirmed"/>.</b> Если юзер привязан к платформе
///             и у него есть active plan-grant на один из bound планов чата (binding с
///             <c>EnrollmentGrantsMembership=true</c> — та же резолюция, что в
///             <see cref="ChatJoinRequestHandler"/>): постит приветствие плана в ГРУППУ через
///             <see cref="PlanWelcomeService"/> (dedup-store <c>tg:welcome:{plan}:{user}</c> общий с
///             approve-путём — двойного приветствия нет) и публикует <see cref="ChatMemberConfirmed"/>
///             по одному на каждый matched plan. Срабатывает <b>независимо</b> от
///             <c>EnforceMembership</c> (welcome ≠ enforcement).
///         </item>
///         <item>
///             <b>F5 enforcement (kick).</b> Если binding имеет <c>EnforceMembership=true</c> и у юзера
///             нет активного <see cref="PlanGrantDto"/> ни на один из enforce-планов чата → бот кикает.
///             По умолчанию <c>EnforceMembership=false</c> — opt-in per-binding (социально болезненна).
///         </item>
///     </list>
///
///     Не применяется для каналов (<c>chat_member</c> не доставляется боту для обычных
///     subscriber'ов канала, только для админов).
/// </summary>
public sealed class ChatMemberUpdateHandler
{

    private readonly IUserLinkRepository _userLinks;
    private readonly IChatBindingRepository _bindings;
    private readonly IAccessServiceClient _accessClient;
    private readonly IChatAdministrationApi _chatApi;
    private readonly PlanWelcomeService _planWelcome;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<ChatMemberUpdateHandler> _logger;

    public ChatMemberUpdateHandler(
        IUserLinkRepository userLinks,
        IChatBindingRepository bindings,
        IAccessServiceClient accessClient,
        IChatAdministrationApi chatApi,
        PlanWelcomeService planWelcome,
        IOutboxService outbox,
        ITransactionManager transactions,
        ILogger<ChatMemberUpdateHandler> logger)
    {
        _userLinks = userLinks;
        _bindings = bindings;
        _accessClient = accessClient;
        _chatApi = chatApi;
        _planWelcome = planWelcome;
        _outbox = outbox;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task<IEndpointResult> HandleAsync(UpdateContext ctx)
    {
        ChatMemberUpdated? update = ctx.Update.ChatMember;
        if (update is null)
            return BotResults.Empty();

        // Реагируем только на «стал участником» — игнорируем left/kicked/restricted-leave/admin.
        if (!IsActiveJoin(update.NewChatMember.Status))
            return BotResults.Empty();

        // Игнорируем добавление самого бота — это обработает MyChatMember handler.
        if (update.NewChatMember.User.IsBot)
            return BotResults.Empty();

        long chatId = update.Chat.Id;
        long userId = update.NewChatMember.User.Id;

        // Все binding'и чата (любой flag) — welcome/event смотрят на EnrollmentGrantsMembership,
        // enforcement — на EnforceMembership. Один fetch на оба пути.
        IReadOnlyList<ChatBinding> chatBindings = await _bindings.GetManyByAsync(
            x => x.TelegramChatId == chatId, ctx.CancellationToken);

        if (chatBindings.Count == 0)
            return BotResults.Empty();

        // Резолвим платформенный аккаунт + active plan-grant'ы один раз (используют оба пути).
        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.TelegramUserId == userId, ctx.CancellationToken);

        if (linkResult.IsFailure)
        {
            // Не привязан к платформе → нет grant'ов: welcome/event не идут.
            // F5 enforcement по-прежнему кикает не-членов в enforce-чатах.
            await EnforceMembershipAsync(chatBindings, chatId, userId, "no platform link", ctx.CancellationToken);
            return BotResults.Empty();
        }

        UserLink link = linkResult.Value;

        Result<IReadOnlyList<PlanGrantDto>, Error> grantsResult =
            await _accessClient.GetUserGrantsAsync(link.PlatformUserId, ctx.CancellationToken);

        if (grantsResult.IsFailure)
        {
            // AccessService недоступен — НЕ кикаем (fail-open для UX) и не постим welcome.
            _logger.LogWarning(
                "chat_member join handling skipped for chat {ChatId} user {UserId}: AccessService failed ({Code})",
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
            _logger.LogWarning(
                "chat_member join handling skipped for chat {ChatId} user {UserId}: Telegram access resolution failed ({Code})",
                chatId, userId, accessResult.Error.Messages[0].Code);
            return BotResults.Empty();
        }

        IReadOnlySet<Guid> activePlanIds = accessResult.Value.Keys.ToHashSet();

        // Genuine NEW active join? (previous status был non-member). Welcome/event только тогда —
        // обычный «refresh» статуса участника не должен спамить приветствием/event'ом.
        bool isGenuineNewJoin = IsGenuineNewJoin(update);

        if (isGenuineNewJoin)
        {
            // (1) Welcome + ChatMemberConfirmed — независимо от EnforceMembership.
            Guid[] matchedPlanIds = chatBindings
                .Where(b => b.EnrollmentGrantsMembership && activePlanIds.Contains(b.PlanId))
                .Select(b => b.PlanId)
                .Distinct()
                .ToArray();

            if (matchedPlanIds.Length > 0)
            {
                await PublishMemberConfirmedAsync(link.PlatformUserId, matchedPlanIds, chatId, ctx.CancellationToken);
                await PostPlanWelcomeInGroupAsync(chatId, link.TelegramUserId, matchedPlanIds, ctx.CancellationToken);
            }
        }

        // (2) F5 enforcement — kick если нет grant'а ни на один enforce-план чата.
        bool hasGrantForEnforcePlan = chatBindings
            .Where(b => b.EnforceMembership)
            .Any(b => activePlanIds.Contains(b.PlanId));

        bool chatHasEnforceBinding = chatBindings.Any(b => b.EnforceMembership);
        if (chatHasEnforceBinding && !hasGrantForEnforcePlan)
        {
            await KickAsync(chatId, userId, "no active plan grant", ctx.CancellationToken);
        }

        return BotResults.Empty();
    }

    /// <summary>
    ///     Публикует <see cref="ChatMemberConfirmed"/> по одному на каждый matched план (зеркалит
    ///     <see cref="ChatJoinRequestHandler"/>), затем флэшит outbox через
    ///     <see cref="ITransactionManager"/> — без flush'а envelope'ы дропнулись бы при dispose
    ///     DbContext'а (см. docs/agents/wolverine-tests.md).
    /// </summary>
    private async Task PublishMemberConfirmedAsync(
        Guid platformUserId, Guid[] matchedPlanIds, long chatId, CancellationToken ct)
    {
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        foreach (Guid planId in matchedPlanIds)
        {
            await _outbox.PublishAsync(
                new ChatMemberConfirmed(platformUserId, planId, chatId, occurredAt));
        }

        UnitResult<Error> flush = await _transactions.SaveChangesAsync(ct);
        if (flush.IsFailure)
        {
            _logger.LogError(
                "Failed to flush ChatMemberConfirmed outbox for chat {ChatId} user {UserId}: {Code}",
                chatId, platformUserId, flush.Error.Messages[0].Code);
            throw flush.Error.AsTransient().ToException();
        }
        else
        {
            _logger.LogInformation(
                "Published ChatMemberConfirmed for user {UserId} on {PlanCount} plan(s) via chat_member join in chat {ChatId}",
                platformUserId, matchedPlanIds.Length, chatId);
        }
    }

    /// <summary>
    ///     Постит настроенное автором приветствие плана в группу при вступлении участника.
    ///     Делегирует в <see cref="PlanWelcomeService"/> (dedup per (user, plan), best-effort).
    ///     Dedup-store общий с <see cref="ChatJoinRequestHandler"/> — если бот сам аппрувнул join,
    ///     второго приветствия не будет. Берёт первый из <paramref name="matchedPlanIds"/> план,
    ///     у которого есть welcome.
    /// </summary>
    private async Task PostPlanWelcomeInGroupAsync(
        long chatId, long telegramUserId, Guid[] matchedPlanIds, CancellationToken ct)
    {
        foreach (Guid planId in matchedPlanIds)
        {
            WelcomeOutcome outcome = await _planWelcome.TrySendWelcomeAsync(
                planId, telegramUserId, chatId, WelcomeDestination.Group, ct, force: false);

            // Sent / AlreadySent / SendFailed — план «обработан», дальше не идём (один welcome
            // на join). NoWelcomeConfigured — у плана нет приветствия, пробуем следующий.
            if (outcome != WelcomeOutcome.NoWelcomeConfigured)
                return;
        }
    }

    /// <summary>
    ///     F5 enforcement: кикает юзера если у него нет активного grant'а ни на один enforce-план
    ///     чата. Вызывается из ветки «нет UserLink» (точно нет grant'ов) с пустым набором planIds.
    /// </summary>
    private async Task EnforceMembershipAsync(
        IReadOnlyList<ChatBinding> chatBindings, long chatId, long userId, string reason, CancellationToken ct)
    {
        if (chatBindings.Any(b => b.EnforceMembership))
        {
            await KickAsync(chatId, userId, reason, ct);
        }
    }

    private async Task KickAsync(long chatId, long userId, string reason, CancellationToken ct)
    {
        ChatApiResult<bool> kickResult = await _chatApi.KickChatMemberAsync(chatId, userId, ct);
        if (kickResult.IsFailure)
        {
            _logger.LogWarning(
                "Enforce-membership kick failed. ChatId={ChatId} UserId={UserId} Reason={Reason} Code={Code}",
                chatId, userId, reason, kickResult.ErrorCode);
        }
        else
        {
            _logger.LogInformation(
                "Enforce-membership kicked user {UserId} from chat {ChatId}: {Reason}",
                userId, chatId, reason);
        }
    }

    /// <summary>
    ///     Статус после update'а — активное членство (стал участником чата).
    ///     Используется F5 enforcement: Restricted-not-member должен по-прежнему отлавливаться,
    ///     поэтому набор шире, чем у <see cref="IsGenuineNewJoin"/>.
    /// </summary>
    private static bool IsActiveJoin(Telegram.Bot.Types.Enums.ChatMemberStatus status) =>
        status switch
        {
            Telegram.Bot.Types.Enums.ChatMemberStatus.Member => true,
            Telegram.Bot.Types.Enums.ChatMemberStatus.Restricted => true,
            _ => false
        };

    /// <summary>
    ///     Genuine NEW active join: старый статус — non-member (Left/Kicked или отсутствует),
    ///     новый — реальное членство (member/administrator/creator). Так отсекаем «refresh»
    ///     уже-участника, чтобы не дублировать welcome/event на каждый чих Telegram'а.
    /// </summary>
    private static bool IsGenuineNewJoin(ChatMemberUpdated update)
    {
        bool wasMember = IsMemberStatus(update.OldChatMember?.Status);
        bool isMemberNow = IsMemberStatus(update.NewChatMember.Status);
        return isMemberNow && !wasMember;
    }

    private static bool IsMemberStatus(Telegram.Bot.Types.Enums.ChatMemberStatus? status) =>
        status switch
        {
            Telegram.Bot.Types.Enums.ChatMemberStatus.Member => true,
            Telegram.Bot.Types.Enums.ChatMemberStatus.Administrator => true,
            Telegram.Bot.Types.Enums.ChatMemberStatus.Creator => true,
            // Restricted member может быть IsMember=true|false — здесь намеренно не считаем
            // genuine-join'ом (welcome/event только на чистый member/admin/creator).
            _ => false
        };
}
