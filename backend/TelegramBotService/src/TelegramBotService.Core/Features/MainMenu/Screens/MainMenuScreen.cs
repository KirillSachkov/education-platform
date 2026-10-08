using System.Globalization;
using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Screens;
using TelegramBotService.Core.Common;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Features.CourseChats.Screens;
using TelegramBotService.Core.Options;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.MainMenu.Screens;

/// <summary>
///     Главный экран бота. Два режима:
///
///     <b>Unlinked (воронка):</b> hero-сообщение про платформу, кнопки «О платформе»,
///     «Войти/регистрация», «Уже в чате курса?» (для F3 reverse helper). Минимум функций —
///     цель сконвертировать.
///
///     <b>Linked (full):</b> greeting со статусом (X планов, Y чатов доступно), кнопки
///     «Мои чаты», «Получить доступ к курсу из чата», «Открыть платформу».
///
///     URL-кнопки рендерятся только для публично резолвимых доменов (см. <see cref="UrlGuard"/>),
///     иначе Telegram отбивает 400 Bad Request.
/// </summary>
public sealed class MainMenuScreen : IScreen
{

    private readonly IUserLinkRepository _userLinks;
    private readonly IChatBindingRepository _bindings;
    private readonly IAccessServiceClient _accessClient;
    private readonly TelegramNotificationOptions _options;
    private readonly ILogger<MainMenuScreen> _logger;

    public MainMenuScreen(
        IUserLinkRepository userLinks,
        IChatBindingRepository bindings,
        IAccessServiceClient accessClient,
        IOptions<TelegramNotificationOptions> options,
        ILogger<MainMenuScreen> logger)
    {
        _userLinks = userLinks;
        _bindings = bindings;
        _accessClient = accessClient;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<ScreenView> RenderAsync(UpdateContext ctx)
    {
        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.TelegramUserId == ctx.UserId, ctx.CancellationToken);

        if (linkResult.IsFailure)
        {
            return RenderUnlinkedView();
        }

        return await RenderLinkedViewAsync(linkResult.Value, ctx.CancellationToken);
    }

    private ScreenView RenderUnlinkedView()
    {
        ScreenView view = new ScreenView(
            "👋 <b>Привет!</b>\n\n" +
            "Это бот образовательной платформы. Через него ты будешь:\n\n" +
            "• 📬 Получать уведомления о новых уроках и проверках\n" +
            "• 💬 Автоматически попадать в Telegram-чаты планов\n" +
            "• 🔓 Получать доступ к плану через членство в чате\n\n" +
            "Чтобы начать — <b>привяжи аккаунт платформы</b>.");

        view.NavigateButton<AboutPlatformScreen>("📚 Что это даёт?").Row();

        // /bot-link — спец-страница: после логина автоматически тригерит link-flow
        // (генерит token, редиректит на t.me/<bot>?start=link_<token>). Это «one-click»
        // воронка для конверсии: юзер нажимает 1 раз — попадает на платформу — логинится —
        // и его сразу возвращает обратно в бот привязанным.
        string oneClickLinkUrl = BuildAbsoluteUrl("/bot-link");
        if (UrlGuard.IsPublic(oneClickLinkUrl))
            view.UrlButton("🔑 Войти и привязать", oneClickLinkUrl).Row();

        view.NavigateButton<ClaimAccessHelperScreen>("🔓 Я уже в чате").Row();

        string platformUrl = BuildAbsoluteUrl("/");
        if (UrlGuard.IsPublic(platformUrl))
            view.UrlButton("🌐 Открыть платформу", platformUrl);

        return view;
    }

    private async Task<ScreenView> RenderLinkedViewAsync(UserLink link, CancellationToken ct)
    {
        (int accessibleChats, int activePlans) = await GetStatusSummaryAsync(link.PlatformUserId, ct);

        string greeting = string.IsNullOrEmpty(link.TelegramUsername)
            ? "👋 Привет!"
            : $"👋 Привет, @{link.TelegramUsername}!";

        string statusLine = activePlans == 0
            ? "Пока нет планов — открой платформу и приобрети любой."
            : $"<b>Планов:</b> {activePlans.ToString(CultureInfo.InvariantCulture)}, " +
              $"<b>чатов доступно:</b> {accessibleChats.ToString(CultureInfo.InvariantCulture)}";

        ScreenView view = new ScreenView(
            $"{greeting}\n\n" +
            "✅ Аккаунт привязан.\n" +
            $"{statusLine}\n\n" +
            "Что хочешь сделать?")
            .NavigateButton<MyChatsScreen>("💬 Мои чаты")
            .Row()
            .NavigateButton<ClaimAccessHelperScreen>("🔓 Получить доступ из чата");

        string platformUrl = BuildAbsoluteUrl("/");
        if (UrlGuard.IsPublic(platformUrl))
            view.Row().UrlButton("🌐 Открыть платформу", platformUrl);

        return view;
    }

    private async Task<(int chats, int plans)> GetStatusSummaryAsync(Guid platformUserId, CancellationToken ct)
    {
        try
        {
            Result<IReadOnlyList<PlanGrantDto>, Error> grantsResult =
                await _accessClient.GetUserGrantsAsync(platformUserId, ct);

            if (grantsResult.IsFailure)
            {
                _logger.LogWarning(
                    "MainMenu status summary: AccessService failed for user {UserId} ({Code})",
                    platformUserId, grantsResult.Error.Messages[0].Code);
                return (0, 0);
            }

            Result<IReadOnlyDictionary<Guid, PlanGrantDto>, Error> accessResult =
                await TelegramGrantChatAccessResolver.ResolveAsync(grantsResult.Value, _accessClient, ct);
            if (accessResult.IsFailure)
            {
                _logger.LogWarning(
                    "MainMenu Telegram access resolution failed for user {UserId} ({Code})",
                    platformUserId,
                    accessResult.Error.Messages[0].Code);
                return (0, 0);
            }

            IReadOnlySet<Guid> activePlanIds = accessResult.Value.Keys.ToHashSet();

            if (activePlanIds.Count == 0)
                return (0, 0);

            IReadOnlyList<ChatBinding> accessibleBindings = await _bindings.GetManyByAsync(
                x => x.EnrollmentGrantsMembership && activePlanIds.Contains(x.PlanId), ct);

            int accessibleChats = accessibleBindings
                .Select(b => b.TelegramChatId)
                .Distinct()
                .Count();
            return (accessibleChats, activePlanIds.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MainMenu status summary failed for user {UserId}", platformUserId);
            return (0, 0);
        }
    }

    private string BuildAbsoluteUrl(string relative)
    {
        string baseUrl = (_options.FrontendBaseUrl ?? string.Empty).TrimEnd('/');
        return baseUrl.Length == 0 ? relative : $"{baseUrl}{relative}";
    }
}
