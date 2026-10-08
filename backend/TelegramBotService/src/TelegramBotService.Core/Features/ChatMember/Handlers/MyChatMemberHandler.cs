using System.Globalization;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Messaging;
using TelegramBotFlow.Core.Routing;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Core.Features.ChatMember.Handlers;

/// <summary>
///     Обрабатывает <c>my_chat_member</c> для бота:
///     • promotion в admin'ы / add как member → шлёт сообщение с chat_id (админу для копи-пасты при bind'е);
///       также если binding для этого чата уже есть — re-validate (кейс: бот сначала добавили без прав,
///       потом дали — health сам поднимется).
///     • kicked / left / restricted (потеряли права) → mark all bindings for this chat as unhealthy
///       без extra API-вызова — Telegram сам сообщил, поэтому нам не нужно дёргать getChatMember.
/// </summary>
public sealed class MyChatMemberHandler
{
    private readonly IBotNotifier _notifier;
    private readonly ChatBindingHealthService _bindingHealth;
    private readonly IChatBindingRepository _bindings;
    private readonly ClaimAnnouncementService _announcer;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<MyChatMemberHandler> _logger;

    public MyChatMemberHandler(
        IBotNotifier notifier,
        ChatBindingHealthService bindingHealth,
        IChatBindingRepository bindings,
        ClaimAnnouncementService announcer,
        ITransactionManager transactions,
        ILogger<MyChatMemberHandler> logger)
    {
        _notifier = notifier;
        _bindingHealth = bindingHealth;
        _bindings = bindings;
        _announcer = announcer;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task<IEndpointResult> HandleAsync(UpdateContext ctx)
    {
        ChatMemberUpdated? update = ctx.Update.MyChatMember;
        if (update is null)
            return BotResults.Empty();

        Telegram.Bot.Types.Enums.ChatType chatType = update.Chat.Type;
        if (chatType is not (Telegram.Bot.Types.Enums.ChatType.Group
                            or Telegram.Bot.Types.Enums.ChatType.Supergroup
                            or Telegram.Bot.Types.Enums.ChatType.Channel))
        {
            return BotResults.Empty();
        }

        long chatId = update.Chat.Id;
        ChatMemberStatus newStatus = update.NewChatMember.Status;

        // Mark unhealthy реактивно — НЕ дёргаем getChatMember.
        if (IsLostAccess(newStatus))
        {
            string reason = newStatus switch
            {
                ChatMemberStatus.Kicked => "bot_kicked_from_chat",
                ChatMemberStatus.Left => "bot_left_chat",
                ChatMemberStatus.Restricted => "bot_restricted",
                ChatMemberStatus.Member => "bot_demoted_to_member",
                _ => "bot_lost_admin",
            };

            await _bindingHealth.MarkUnhealthyAsync(chatId, reason, ctx.CancellationToken);
            return BotResults.Empty();
        }

        if (!IsActiveJoin(newStatus))
            return BotResults.Empty();

        // Бот стал admin / member. Если чат уже привязан к плану(ам) с claim-флоу и
        // claim-объявление ещё не постили — постим закреплённую кнопку (фича A, #410).
        // Кейс: бота добавили в УЖЕ привязанный чат. Unhealthy binding'и поднимет periodic в ≤ 6h.
        IReadOnlyList<ChatBinding> claimable = await _bindings.GetManyByAsync(
            b => b.TelegramChatId == chatId && b.MembershipGrantsEnrollment && b.AnnouncementMessageId == null,
            ctx.CancellationToken);

        if (claimable.Count > 0)
        {
            foreach (ChatBinding binding in claimable)
            {
                await _announcer.TryPostAndMarkAsync(binding, ctx.CancellationToken);

                // GetManyByAsync отдаёт untracked entities — без явного Update изменение
                // AnnouncementMessageId не персистится (см. IChatBindingRepository.Update). #434.
                if (binding.AnnouncementMessageId is not null)
                    _bindings.Update(binding);
            }

            UnitResult<Error> save = await _transactions.SaveChangesAsync(ctx.CancellationToken);
            if (save.IsFailure)
            {
                _logger.LogError(
                    "Failed to persist claim-announcement message ids for chat {ChatId}: {Code}",
                    chatId, save.Error.Messages[0].Code);
            }

            return BotResults.Empty();
        }

        // Чат не привязан ни к одному плану — шлём chat_id админу для копи-пасты при привязке.
        string? title = update.Chat.Title;
        try
        {
            string text = "👋 Привет! Я добавлен в этот чат."
                + $"\n\nChat ID: <code>{chatId.ToString(CultureInfo.InvariantCulture)}</code>"
                + (string.IsNullOrEmpty(title) ? string.Empty : $"\nName: {title}")
                + "\n\nСкопируй ID и используй на платформе при привязке к плану. " +
                  "Для повторного запроса напиши <code>/chatid</code>.";

            await _notifier.SendTextAsync(
                chatId, text, parseMode: ParseMode.Html, ct: ctx.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Бот может не иметь права писать в канал/группу до promotion'а — это нормально.
            _logger.LogInformation(ex,
                "Failed to send chat-id message to {ChatType} {ChatId} (likely no can_send_messages yet)",
                chatType, chatId);
        }

        return BotResults.Empty();
    }

    private static bool IsActiveJoin(ChatMemberStatus status) =>
        status switch
        {
            ChatMemberStatus.Member => true,
            ChatMemberStatus.Administrator => true,
            ChatMemberStatus.Creator => true,
            _ => false
        };

    private static bool IsLostAccess(ChatMemberStatus status) =>
        status switch
        {
            ChatMemberStatus.Kicked => true,
            ChatMemberStatus.Left => true,
            ChatMemberStatus.Restricted => true,
            // NB: Member status сам по себе не плохой — но если бот ДО этого был админом, это demotion.
            //     ChatMemberUpdated.OldChatMember.Status даст контекст, но проще: при demotion периодический
            //     check всё равно перехватит. Не усложняем.
            _ => false
        };
}
