using System.Globalization;
using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotService.Core.Features.CourseChats.Handlers;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Core.Features.CourseChats.Services;

/// <summary>
///     Фича A (#410, расширено #416): постит закреплённое claim-объявление В ЧАТ ИЛИ КАНАЛ,
///     привязанный к плану, с inline-кнопкой-deeplink «Привязать аккаунт и получить доступ»
///     (ведёт в существующий F3 claim-флоу <c>/start claim_chat_&lt;chatId&gt;</c>). Цель —
///     чтобы уже состоящие в чате / подписанные на канал участники узнали, что могут забрать доступ.
///
///     Работает одинаково для групп/супергрупп и для <b>закрытых каналов</b>: бот шлёт сообщение
///     через <see cref="ITelegramBotClient.SendMessage"/> (в канале требует право
///     <c>can_post_messages</c>) и best-effort закрепляет (<c>can_pin_messages</c>). Membership при
///     клике проверяется on-demand в <see cref="ClaimCourseAccessHandler"/> через
///     <c>getChatMember</c> — для канала это работает, если бот админ.
///
///     Идемпотентно: пропускает, если объявление уже постили (<see cref="ChatBinding.AnnouncementMessageId"/>
///     не null) или reverse-claim выключен (<see cref="ChatBinding.MembershipGrantsEnrollment"/> == false).
///     Best-effort — НЕ бросает исключений; мутирует переданный (tracked) <see cref="ChatBinding"/>
///     через <see cref="ChatBinding.MarkAnnouncementPosted"/>, а сохранение — на стороне вызывающего.
/// </summary>
public sealed class ClaimAnnouncementService
{
    private readonly ITelegramBotClient _bot;
    private readonly IAccessServiceClient _accessClient;
    private readonly ILogger<ClaimAnnouncementService> _logger;

    public ClaimAnnouncementService(
        ITelegramBotClient bot,
        IAccessServiceClient accessClient,
        ILogger<ClaimAnnouncementService> logger)
    {
        _bot = bot;
        _accessClient = accessClient;
        _logger = logger;
    }

    public async Task TryPostAndMarkAsync(ChatBinding binding, CancellationToken ct)
    {
        if (binding.AnnouncementMessageId is not null)
        {
            return; // уже постили
        }

        if (!binding.MembershipGrantsEnrollment)
        {
            return; // reverse-claim выключен — claim-кнопка бессмысленна
        }

        bool isChannel = binding.ChatType == ChatType.CHANNEL;

        try
        {
            User me = await _bot.GetMe(ct);
            if (string.IsNullOrEmpty(me.Username))
            {
                _logger.LogWarning(
                    "Cannot post claim announcement in chat {ChatId}: bot has no username", binding.TelegramChatId);
                return;
            }

            string offer = await ResolveOfferNameAsync(binding.PlanId, ct);
            string deepLink =
                $"https://t.me/{me.Username}?start={ClaimCourseAccessHandler.PAYLOAD_PREFIX}" +
                binding.TelegramChatId.ToString(CultureInfo.InvariantCulture);

            string place = isChannel ? "Этот канал открывает" : "Этот чат открывает";
            string membership = isChannel ? "Уже подписан здесь?" : "Уже состоишь здесь?";
            string text =
                $"📚 {place} доступ к {offer} на платформе.\n\n" +
                $"{membership} Нажми кнопку ниже — привяжи аккаунт платформы и забери доступ к материалам.";

            InlineKeyboardMarkup keyboard =
                new(InlineKeyboardButton.WithUrl("🔓 Привязать аккаунт и получить доступ", deepLink));

            Message msg = await _bot.SendMessage(
                binding.TelegramChatId,
                text,
                replyMarkup: keyboard,
                cancellationToken: ct);

            try
            {
                await _bot.PinChatMessage(
                    binding.TelegramChatId, msg.MessageId, disableNotification: true, cancellationToken: ct);
            }
            catch (Exception pinEx) when (pinEx is not OperationCanceledException)
            {
                _logger.LogInformation(pinEx,
                    "Posted claim announcement but failed to pin it in chat {ChatId} (bot may lack can_pin_messages)",
                    binding.TelegramChatId);
            }

            binding.MarkAnnouncementPosted(msg.MessageId);

            _logger.LogInformation(
                "Posted claim announcement in {ChatKind} {ChatId} for plan {PlanId} (messageId={MessageId})",
                isChannel ? "channel" : "chat", binding.TelegramChatId, binding.PlanId, msg.MessageId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Канал без can_post_messages / чат без права писать → SendMessage кидает ApiRequestException.
            // Best-effort: логируем, не валим bind/update. AnnouncementMessageId остаётся null —
            // UI покажет «объявление не опубликовано, проверьте права бота».
            _logger.LogWarning(ex,
                "Failed to post claim announcement in {ChatKind} {ChatId} for plan {PlanId} " +
                "(bot may lack can_post_messages or is not admin)",
                isChannel ? "channel" : "chat", binding.TelegramChatId, binding.PlanId);
        }
    }

    private async Task<string> ResolveOfferNameAsync(Guid planId, CancellationToken ct)
    {
        Result<PlanTelegramInfoDto, Error> info = await _accessClient.GetPlanTelegramInfoAsync(planId, ct);
        if (info.IsSuccess && !string.IsNullOrWhiteSpace(info.Value.DisplayName))
        {
            return $"плану «{info.Value.DisplayName}»";
        }

        return "материалам";
    }
}
