using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Common;
using TelegramBotService.Core.Options;

namespace TelegramBotService.Core.Features.CourseChats.Services;

/// <summary>Куда слать приветствие плана, если пользователь уже состоит в чате.</summary>
public enum WelcomeDestination
{
    /// <summary>В личку пользователю; при сбое DM — fallback'ом постом в группу.</summary>
    DirectMessage = 0,

    /// <summary>Прямо в группу (используется на approve join-request'а — публичное приветствие).</summary>
    Group = 1,
}

/// <summary>Результат попытки отправить приветствие.</summary>
public enum WelcomeOutcome
{
    Sent = 0,
    AlreadySent = 1,
    NoWelcomeConfigured = 2,
    SendFailed = 3,
}

/// <summary>
///     Единая точка отправки настроенного автором приветствия плана
///     (<c>Plan.telegram_welcome_message</c>, #411) ровно один раз на пару (user, plan).
///
///     Корень бага #444 (symptom #1): приветствие постилось ТОЛЬКО при approve'е
///     <c>chat_join_request</c> — а уже-участник чата (купил, будучи в группе; добавлен
///     админом; вступил по старому инвайту) join-request не генерирует, поэтому welcome
///     не приходил никогда. Теперь сервис зовётся из ВСЕХ путей подтверждённого членства,
///     которыми владеет TelegramBotService: approve join-request (<see cref="WelcomeDestination.Group"/>),
///     <c>plan_grant.created</c> для уже-участника и resync инвайтов
///     (<see cref="WelcomeDestination.DirectMessage"/>). Dedup через
///     <see cref="IPlanWelcomeSentStore"/> гарантирует exactly-once поверх всех триггеров.
/// </summary>
public sealed class PlanWelcomeService
{
    /// <summary>
    ///     Generic-приветствие, которым бот встречает участника, принятого/вступившего в группу
    ///     курса, когда автор НЕ настроил <c>Plan.telegram_welcome_message</c> (#687). Применяется
    ///     только для <see cref="WelcomeDestination.Group"/> — чтобы принятый в группу участник
    ///     всегда был поприветствован. В личку (DM) generic-фолбэк НЕ шлём, чтобы не спамить.
    /// </summary>
    public const string DEFAULT_GROUP_WELCOME =
        "Добро пожаловать в группу курса! 👋 Рады видеть вас здесь.";

    private readonly IAccessServiceClient _accessClient;
    private readonly IBotNotifier _notifier;
    private readonly IPlanWelcomeSentStore _sentStore;
    private readonly TelegramNotificationOptions _options;
    private readonly ILogger<PlanWelcomeService> _logger;

    public PlanWelcomeService(
        IAccessServiceClient accessClient,
        IBotNotifier notifier,
        IPlanWelcomeSentStore sentStore,
        IOptions<TelegramNotificationOptions> options,
        ILogger<PlanWelcomeService> logger)
    {
        _accessClient = accessClient;
        _notifier = notifier;
        _sentStore = sentStore;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    ///     Отправляет приветствие плана пользователю ровно один раз. <paramref name="groupChatId"/> —
    ///     bound-чат плана (получатель для <see cref="WelcomeDestination.Group"/> и fallback для DM).
    ///     Порядок: атомарное занятие dedup-ключа (<see cref="IPlanWelcomeSentStore.TryMarkSentAsync"/>)
    ///     → резолв текста welcome'а из AccessService → отправка. Занимать ключ нужно ДО отправки:
    ///     один вход в группу даёт два конкурентных Telegram-апдейта (approve join-request +
    ///     chat_member join), и check-then-act пропускал оба — приветствие уходило дважды
    ///     (прод-инцидент 2026-07-05). Если отправка не состоялась, ключ освобождается
    ///     (<see cref="IPlanWelcomeSentStore.UnmarkSentAsync"/>) — транзиентный сбой не должен
    ///     подавить приветствие навсегда; следующий триггер попробует снова.
    ///     Best-effort: ошибки отправки не пробрасываются (caller — handler в Wolverine/TBF pipeline).
    ///     <paramref name="force"/> = true пропускает dedup и шлёт приветствие даже если оно уже
    ///     отправлялось — используется support-action'ом «переотправить приветствие» (#444).
    ///     Пометка sent на успехе ставится в обоих случаях.
    /// </summary>
    public async Task<WelcomeOutcome> TrySendWelcomeAsync(
        Guid planId,
        long telegramUserId,
        long groupChatId,
        WelcomeDestination preferred,
        CancellationToken ct,
        bool force = false)
    {
        // Dedup per (plan, destination, user) — раннее DM-приветствие не подавляет позднее
        // групповое и наоборот (#687). Ключ занимается атомарно до резолва/отправки,
        // проигравший конкурентный триггер выходит сразу.
        if (!force && !await _sentStore.TryMarkSentAsync(planId, telegramUserId, preferred, ct))
            return WelcomeOutcome.AlreadySent;

        Result<PlanTelegramInfoDto, Error> info = await _accessClient.GetPlanTelegramInfoAsync(planId, ct);
        string? welcome = info.IsSuccess ? info.Value.WelcomeMessage : null;

        if (string.IsNullOrWhiteSpace(welcome))
        {
            // Автор не настроил приветствие (или telegram-info lookup упал транзиентно).
            // GROUP: шлём generic-фолбэк, чтобы принятый в группу участник всегда был встречен (#687).
            // DM: остаёмся тихими — не спамим личку generic-сообщением.
            if (preferred != WelcomeDestination.Group)
            {
                if (info.IsFailure)
                {
                    _logger.LogInformation(
                        "Skipping plan welcome DM: telegram-info lookup failed for plan {PlanId}", planId);
                }

                // Ключ освобождаем: когда автор настроит приветствие (или lookup оживёт),
                // следующий триггер должен смочь отправить DM.
                if (!force)
                    await _sentStore.UnmarkSentAsync(planId, telegramUserId, preferred, ct);
                return WelcomeOutcome.NoWelcomeConfigured;
            }

            welcome = DEFAULT_GROUP_WELCOME;
        }

        string platformUrl = BuildAbsoluteUrl("/");
        InlineKeyboardMarkup? keyboard = UrlGuard.IsPublic(platformUrl)
            ? new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("🌐 Открыть платформу", platformUrl))
            : null;

        bool sent;
        if (preferred == WelcomeDestination.DirectMessage)
        {
            sent = await TrySendAsync(telegramUserId, welcome, keyboard, ct);
            if (!sent)
            {
                _logger.LogInformation(
                    "Plan welcome DM to user {UserId} failed (likely never started bot); falling back to group {ChatId}",
                    telegramUserId, groupChatId);
                sent = await TrySendAsync(groupChatId, welcome, keyboard, ct);
            }
        }
        else
        {
            sent = await TrySendAsync(groupChatId, welcome, keyboard, ct);
        }

        if (!sent)
        {
            if (!force)
                await _sentStore.UnmarkSentAsync(planId, telegramUserId, preferred, ct);
            return WelcomeOutcome.SendFailed;
        }

        if (force)
            await _sentStore.TryMarkSentAsync(planId, telegramUserId, preferred, ct);
        _logger.LogInformation(
            "Sent plan welcome for plan {PlanId} to user {UserId} (preferred={Destination})",
            planId, telegramUserId, preferred);
        return WelcomeOutcome.Sent;
    }

    private async Task<bool> TrySendAsync(
        long chatId, string text, InlineKeyboardMarkup? keyboard, CancellationToken ct)
    {
        try
        {
            await _notifier.SendTextAsync(chatId, text, keyboard, ct: ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex, "Plan welcome send to chat {ChatId} failed", chatId);
            return false;
        }
    }

    private string BuildAbsoluteUrl(string relative)
    {
        string baseUrl = (_options.FrontendBaseUrl ?? string.Empty).TrimEnd('/');
        return baseUrl.Length == 0 ? relative : $"{baseUrl}{relative}";
    }
}
