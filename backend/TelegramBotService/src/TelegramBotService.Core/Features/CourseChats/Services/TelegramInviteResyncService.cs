using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Database;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Core.Features.CourseChats.Services;

/// <summary>
///     Общая логика «пере-разослать инвайты в чаты планов»: используется и event-handler'ом
///     <c>UserTelegramLinkedHandler</c> (на user.telegram_linked), и user-facing
///     эндпоинтом <c>POST /telegram/me/resync-invites</c>. Идемпотентно: повторный вызов
///     даёт идентичный набор DM.
/// </summary>
public sealed class TelegramInviteResyncService
{

    private readonly IChatBindingRepository _bindings;
    private readonly IAccessServiceClient _accessClient;
    private readonly IBotNotifier _notifier;
    private readonly PlanWelcomeService _planWelcome;
    private readonly ChatMembershipChecker _membership;
    private readonly ILogger<TelegramInviteResyncService> _logger;

    public TelegramInviteResyncService(
        IChatBindingRepository bindings,
        IAccessServiceClient accessClient,
        IBotNotifier notifier,
        PlanWelcomeService planWelcome,
        ChatMembershipChecker membership,
        ILogger<TelegramInviteResyncService> logger)
    {
        _bindings = bindings;
        _accessClient = accessClient;
        _notifier = notifier;
        _planWelcome = planWelcome;
        _membership = membership;
        _logger = logger;
    }

    /// <summary>
    ///     Возвращает количество отправленных DM-инвайтов. 0 — если нет bindings, нет
    ///     активных plan-grants в bound-планах. Ошибка AccessService возвращается caller-у,
    ///     чтобы Wolverine мог повторить интеграционное событие.
    /// </summary>
    public async Task<Result<int, Error>> ResyncInvitesAsync(
        Guid platformUserId,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<PlanGrantDto>, Error> grantsResult =
            await _accessClient.GetUserGrantsAsync(platformUserId, cancellationToken);

        if (grantsResult.IsFailure)
        {
            _logger.LogWarning(
                "Re-trigger invites failed for user {UserId}: AccessService error {Code}",
                platformUserId, grantsResult.Error.Messages[0].Code);
            return grantsResult.Error;
        }

        Result<IReadOnlyDictionary<Guid, PlanGrantDto>, Error> accessResult =
            await TelegramGrantChatAccessResolver.ResolveAsync(
                grantsResult.Value,
                _accessClient,
                cancellationToken);
        if (accessResult.IsFailure)
            return accessResult.Error;

        IReadOnlySet<Guid> activePlanIds = accessResult.Value.Keys.ToHashSet();

        if (activePlanIds.Count == 0)
            return 0;

        IReadOnlyList<ChatBinding> bindings = await _bindings.GetManyByAsync(
            x => x.EnrollmentGrantsMembership && activePlanIds.Contains(x.PlanId),
            cancellationToken);

        if (bindings.Count == 0)
            return 0;

        var seenChats = new HashSet<long>();
        int sent = 0;
        int skippedAlreadyMember = 0;

        foreach (ChatBinding binding in bindings)
        {
            if (!seenChats.Add(binding.TelegramChatId))
                continue;

            MembershipStatus status = await _membership.GetStatusAsync(
                binding.TelegramChatId, telegramUserId, cancellationToken);
            if (status == MembershipStatus.Member)
            {
                // Уже в чате — invite не нужен, но добиваем ПРОПУЩЕННОЕ приветствие плана
                // (recovery-путь для уже-участников вроде @vokhminov; dedup per (user, plan)
                // не задублирует с другими триггерами). #444 symptom #1.
                skippedAlreadyMember++;
                await _planWelcome.TrySendWelcomeAsync(
                    binding.PlanId, telegramUserId, binding.TelegramChatId,
                    WelcomeDestination.DirectMessage, cancellationToken, force: false);
                continue;
            }

            try
            {
                string title = string.IsNullOrEmpty(binding.ChatTitle) ? "чат плана" : binding.ChatTitle;
                string text = $"🔓 Тебе доступен чат «{title}» — у тебя есть активный план. " +
                              "Жми кнопку ниже, чтобы вступить — бот пустит автоматически.";

                InlineKeyboardMarkup keyboard = new(InlineKeyboardButton.WithUrl(
                    "Войти в чат", binding.InviteLink));

                await _notifier.SendTextAsync(telegramUserId, text, keyboard, ct: cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Re-trigger: failed to DM invite to user {UserId} chat {ChatId}",
                    telegramUserId, binding.TelegramChatId);
            }
        }

        if (skippedAlreadyMember > 0)
        {
            _logger.LogInformation(
                "Re-trigger: skipped {Count} chats for user {UserId} — already a member",
                skippedAlreadyMember, platformUserId);
        }

        if (sent > 0)
        {
            _logger.LogInformation(
                "Re-triggered {Count} chat invites for user {UserId}",
                sent, platformUserId);
        }

        return sent;
    }
}
