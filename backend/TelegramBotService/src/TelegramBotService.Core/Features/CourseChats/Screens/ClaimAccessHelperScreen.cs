using System.Globalization;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Messaging;
using TelegramBotFlow.Core.Screens;
using TelegramBotService.Core.Database;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.CourseChats.Screens;

/// <summary>
///     «Получить доступ к курсу из чата» — F3 reverse helper:
///     - Linked юзер: сканирует все bound chats с <c>MembershipGrantsEnrollment=true</c>,
///       проверяет реальное членство юзера через <c>getChatMember</c>, и показывает кнопки
///       «Получить доступ к курсу X» (deep-link на тот же бот с <c>claim_chat_</c> payload).
///     - Unlinked юзер: показывает CTA «Сначала привяжи аккаунт».
///
///     <b>Performance:</b> N запросов к Telegram API (по числу bound chats). На MVP норм,
///     для масштаба — можно кешировать или ограничить top-50.
/// </summary>
public sealed class ClaimAccessHelperScreen : IScreen
{
    private readonly IUserLinkRepository _userLinks;
    private readonly IChatBindingRepository _bindings;
    private readonly IChatAdministrationApi _chatApi;

    public ClaimAccessHelperScreen(
        IUserLinkRepository userLinks,
        IChatBindingRepository bindings,
        IChatAdministrationApi chatApi,
        ILogger<ClaimAccessHelperScreen> _)
    {
        _userLinks = userLinks;
        _bindings = bindings;
        _chatApi = chatApi;
    }

    public async ValueTask<ScreenView> RenderAsync(UpdateContext ctx)
    {
        long telegramUserId = ctx.UserId;

        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.TelegramUserId == telegramUserId, ctx.CancellationToken);

        if (linkResult.IsFailure)
        {
            return new ScreenView(
                    "🔓 <b>Доступ к плану через чат</b>\n\n" +
                    "Если ты уже в чате какого-то плана, бот может выдать тебе доступ к этому плану " +
                    "на платформе — без оплаты.\n\n" +
                    "Но сначала нужно <b>привязать аккаунт платформы</b> к Telegram. " +
                    "Без аккаунта на платформе доступ просто некуда выдавать.")
                .BackButton();
        }

        IReadOnlyList<ChatBinding> allBindings = await _bindings.GetManyByAsync(
            x => x.MembershipGrantsEnrollment, ctx.CancellationToken);

        if (allBindings.Count == 0)
        {
            return new ScreenView(
                    "🔓 <b>Доступ к плану через чат</b>\n\n" +
                    "Сейчас нет планов, которые выдают доступ через членство в чате.")
                .BackButton();
        }

        // Параллельно проверяем членство. Кратко лог-ним если что-то не пришло.
        var memberChecks = await Task.WhenAll(allBindings.Select(async binding =>
        {
            ChatApiResult<ChatMemberInfo> result = await _chatApi.GetChatMemberAsync(
                binding.TelegramChatId, telegramUserId, ctx.CancellationToken);
            return new { Binding = binding, IsMember = result.IsSuccess && result.Value!.IsActiveMember };
        }));

        var availableBindings = memberChecks
            .Where(x => x.IsMember)
            .Select(x => x.Binding)
            .ToList();

        if (availableBindings.Count == 0)
        {
            return new ScreenView(
                    "🔓 <b>Доступ к плану через чат</b>\n\n" +
                    "Я проверил все планы с включённой опцией. Ты пока не состоишь ни в одном из их чатов. " +
                    "Если только что вступил — попробуй ещё раз через минуту (Telegram задерживает обновления).")
                .BackButton();
        }

        ScreenView view = new(
            "🔓 <b>Доступ к плану через чат</b>\n\n" +
            $"Я нашёл {availableBindings.Count.ToString(CultureInfo.InvariantCulture)} " +
            "план(ов), к которым у тебя есть доступ через членство в чате. " +
            "Нажми чтобы получить доступ на платформе:");

        foreach (ChatBinding binding in availableBindings)
        {
            string title = string.IsNullOrEmpty(binding.ChatTitle) ? "Без названия" : binding.ChatTitle;
            // Deep-link обратно в этот же бот с payload — `LinkAccountEndpoint` диспетчер
            // распарсит `claim_chat_<chatId>` и вызовет ClaimCourseAccessHandler.
            // Юзер увидит подтверждение прямо в этом чате.
            view.Button($"💬 {title}", $"helper:claim:{binding.TelegramChatId.ToString(CultureInfo.InvariantCulture)}").Row();
        }

        view.BackButton();
        return view;
    }
}
