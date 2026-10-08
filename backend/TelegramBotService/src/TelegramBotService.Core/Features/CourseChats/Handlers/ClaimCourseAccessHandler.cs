using System.Globalization;
using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Messaging;
using TelegramBotFlow.Core.Routing;
using TelegramBotService.Core.Common;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Features.MainMenu.Screens;
using TelegramBotService.Core.Options;
using TelegramBotService.Domain.Audit;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.CourseChats.Handlers;

/// <summary>
///     F3 reverse флоу: юзер пишет /start claim_chat_&lt;chatId&gt; в личке боту, бот:
///     1) Проверяет, что у юзера есть UserLink (привязка платформы).
///     2) Проверяет через <c>getChatMember</c>, что юзер реально в этом чате.
///     3) Находит bound plans с <c>membership_grants_enrollment=true</c>.
///     4) Для каждого вызывает AccessService grant-by-plan (Source=TELEGRAM_F1).
///     5) Отчёт в личку: «доступ к плану выдан».
/// </summary>
public sealed class ClaimCourseAccessHandler
{
    public const string PAYLOAD_PREFIX = "claim_chat_";

    private const string SOURCE_TELEGRAM_F1 = "TELEGRAM_F1";

    private readonly IUserLinkRepository _userLinks;
    private readonly IChatBindingRepository _bindings;
    private readonly IChatAdministrationApi _chatApi;
    private readonly IAccessServiceClient _accessClient;
    private readonly IBotNotifier _notifier;
    private readonly IBotDecisionLogger _audit;
    private readonly TelegramNotificationOptions _options;
    private readonly ILogger<ClaimCourseAccessHandler> _logger;

    public ClaimCourseAccessHandler(
        IUserLinkRepository userLinks,
        IChatBindingRepository bindings,
        IChatAdministrationApi chatApi,
        IAccessServiceClient accessClient,
        IBotNotifier notifier,
        IBotDecisionLogger audit,
        IOptions<TelegramNotificationOptions> options,
        ILogger<ClaimCourseAccessHandler> logger)
    {
        _userLinks = userLinks;
        _bindings = bindings;
        _chatApi = chatApi;
        _accessClient = accessClient;
        _notifier = notifier;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IEndpointResult> HandleAsync(UpdateContext ctx)
    {
        string payload = ctx.CommandArgument!;
        string chatIdStr = payload[PAYLOAD_PREFIX.Length..];
        if (!long.TryParse(chatIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long chatId))
        {
            await _notifier.SendTextAsync(
                ctx.ChatId, "Неверный формат ссылки. Получите её в чате плана.", ct: ctx.CancellationToken);
            return BotResults.NavigateToRoot<MainMenuScreen>();
        }

        return await ClaimAsync(ctx, chatId);
    }

    public Task<IEndpointResult> ClaimByChatIdAsync(UpdateContext ctx, long chatId) =>
        ClaimAsync(ctx, chatId);

    private async Task<IEndpointResult> ClaimAsync(UpdateContext ctx, long chatId)
    {
        long telegramUserId = ctx.UserId;

        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.TelegramUserId == telegramUserId, ctx.CancellationToken);
        if (linkResult.IsFailure)
        {
            string profileUrl = BuildAbsoluteUrl("/settings/integrations");
            InlineKeyboardMarkup? keyboard = UrlGuard.IsPublic(profileUrl)
                ? new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("Открыть профиль", profileUrl))
                : null;
            await _notifier.SendTextAsync(
                ctx.ChatId,
                "Сначала привяжи аккаунт платформы, потом нажми ссылку из чата ещё раз.",
                keyboard,
                ct: ctx.CancellationToken);
            return BotResults.NavigateToRoot<MainMenuScreen>();
        }

        UserLink link = linkResult.Value;

        ChatApiResult<ChatMemberInfo> memberResult = await _chatApi.GetChatMemberAsync(
            chatId, telegramUserId, ctx.CancellationToken);
        if (memberResult.IsFailure)
        {
            await _notifier.SendTextAsync(
                ctx.ChatId, "Не удалось проверить членство в чате. Попробуйте позже.",
                ct: ctx.CancellationToken);
            return BotResults.NavigateToRoot<MainMenuScreen>();
        }

        if (!memberResult.Value!.IsActiveMember)
        {
            await _audit.LogAsync(chatId, telegramUserId,
                BotDecisions.CLAIM_DECLINED_NOT_MEMBER, ct: ctx.CancellationToken);
            await _notifier.SendTextAsync(
                ctx.ChatId,
                "Похоже, ты не состоишь в этом чате. Сначала вступи в него, потом возвращайся за доступом.",
                ct: ctx.CancellationToken);
            return BotResults.NavigateToRoot<MainMenuScreen>();
        }

        IReadOnlyList<ChatBinding> bindings = await _bindings.GetManyByAsync(
            x => x.TelegramChatId == chatId && x.MembershipGrantsEnrollment, ctx.CancellationToken);

        if (bindings.Count == 0)
        {
            await _audit.LogAsync(chatId, telegramUserId,
                BotDecisions.CLAIM_DECLINED_NO_BINDING, ct: ctx.CancellationToken);
            await _notifier.SendTextAsync(
                ctx.ChatId,
                "Этот чат не выдаёт доступ к плану. Если ты считаешь, что это ошибка — напиши автору.",
                ct: ctx.CancellationToken);
            return BotResults.NavigateToRoot<MainMenuScreen>();
        }

        List<string> granted = [];
        List<string> alreadyHad = [];
        List<string> failed = [];

        foreach (ChatBinding binding in bindings)
        {
            Result<PlanGrantDto, Error> grantResult =
                await _accessClient.GrantByPlanAsync(
                    link.PlatformUserId, binding.PlanId, SOURCE_TELEGRAM_F1, sourceRef: null, ctx.CancellationToken);

            string planLabel = binding.PlanId.ToString("N", CultureInfo.InvariantCulture);
            if (grantResult.IsFailure)
            {
                failed.Add(planLabel);
                _logger.LogWarning(
                    "Grant-by-plan failed. UserId={UserId} PlanId={PlanId} Code={Code}",
                    link.PlatformUserId, binding.PlanId, grantResult.Error.Messages[0].Code);
                continue;
            }

            // Idempotent endpoint returns existing grant if user already had ACTIVE one;
            // we treat this as ALREADY_ENROLLED. New grants — отметим как CLAIM_GRANTED.
            // GrantedAt < now-1min — heuristic: pre-existing grant.
            bool isFresh = (DateTimeOffset.UtcNow - grantResult.Value.GrantedAt) < TimeSpan.FromMinutes(1);
            if (isFresh)
            {
                granted.Add(planLabel);
                await _audit.LogAsync(chatId, telegramUserId, BotDecisions.CLAIM_GRANTED,
                    planId: binding.PlanId, ct: ctx.CancellationToken);
            }
            else
            {
                alreadyHad.Add(planLabel);
                await _audit.LogAsync(chatId, telegramUserId, BotDecisions.CLAIM_ALREADY_ENROLLED,
                    planId: binding.PlanId, ct: ctx.CancellationToken);
            }
        }

        await SendOutcomeAsync(ctx, granted, alreadyHad, failed);
        return BotResults.NavigateToRoot<MainMenuScreen>();
    }

    private async Task SendOutcomeAsync(
        UpdateContext ctx,
        List<string> granted,
        List<string> alreadyHad,
        List<string> failed)
    {
        string text;
        if (granted.Count > 0 && alreadyHad.Count == 0)
        {
            text = granted.Count == 1
                ? "✅ Доступ к плану выдан. Открой платформу, чтобы начать обучение."
                : $"✅ Доступ выдан к {granted.Count.ToString(CultureInfo.InvariantCulture)} планам.";
        }
        else if (granted.Count == 0 && alreadyHad.Count > 0)
        {
            text = alreadyHad.Count == 1
                ? "ℹ️ У тебя уже есть активный доступ. Открой платформу, чтобы продолжить обучение."
                : $"ℹ️ У тебя уже есть доступ ко всем {alreadyHad.Count.ToString(CultureInfo.InvariantCulture)} планам этого чата.";
        }
        else if (granted.Count > 0 && alreadyHad.Count > 0)
        {
            text = $"✅ Выдано: {granted.Count.ToString(CultureInfo.InvariantCulture)}. " +
                   $"Уже было: {alreadyHad.Count.ToString(CultureInfo.InvariantCulture)}.";
        }
        else if (failed.Count > 0)
        {
            await _notifier.SendTextAsync(
                ctx.ChatId,
                "❌ Не удалось выдать доступ. Попробуй позже или напиши автору плана.",
                ct: ctx.CancellationToken);
            return;
        }
        else
        {
            return;
        }

        string coursesUrl = BuildAbsoluteUrl("/courses");
        InlineKeyboardMarkup? keyboard = UrlGuard.IsPublic(coursesUrl)
            ? new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("Мои курсы", coursesUrl))
            : null;

        await _notifier.SendTextAsync(ctx.ChatId, text, keyboard, ct: ctx.CancellationToken);
    }

    private string BuildAbsoluteUrl(string relative)
    {
        string baseUrl = (_options.FrontendBaseUrl ?? string.Empty).TrimEnd('/');
        return baseUrl.Length == 0 ? relative : $"{baseUrl}{relative}";
    }
}
