using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Screens;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.CourseChats.Screens;

/// <summary>
///     Экран «Мои чаты» в боте: показывает список TG-чатов, к которым у юзера есть доступ
///     через активный <c>PlanGrant</c> на план чата, и invite-кнопки для перехода.
///     Обновляется на каждом render'е (нет кэша) — юзер всегда видит актуальное.
/// </summary>
public sealed class MyChatsScreen : IScreen
{

    private readonly IUserLinkRepository _userLinks;
    private readonly IChatBindingRepository _bindings;
    private readonly IAccessServiceClient _accessClient;
    private readonly ILogger<MyChatsScreen> _logger;

    public MyChatsScreen(
        IUserLinkRepository userLinks,
        IChatBindingRepository bindings,
        IAccessServiceClient accessClient,
        ILogger<MyChatsScreen> logger)
    {
        _userLinks = userLinks;
        _bindings = bindings;
        _accessClient = accessClient;
        _logger = logger;
    }

    public async ValueTask<ScreenView> RenderAsync(UpdateContext ctx)
    {
        long telegramUserId = ctx.UserId;

        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.TelegramUserId == telegramUserId, ctx.CancellationToken);
        if (linkResult.IsFailure)
        {
            return new ScreenView(
                    "Сначала привяжи аккаунт платформы — после этого тут появятся доступные чаты планов.")
                .BackButton();
        }

        // 1) Активные plan-grants юзера через AccessService.
        Result<IReadOnlyList<PlanGrantDto>, Error> grantsResult =
            await _accessClient.GetUserGrantsAsync(linkResult.Value.PlatformUserId, ctx.CancellationToken);

        if (grantsResult.IsFailure)
        {
            _logger.LogWarning(
                "AccessService GetUserGrants failed for user {UserId}: {Code}",
                linkResult.Value.PlatformUserId, grantsResult.Error.Messages[0].Code);
            return new ScreenView(
                    "Не удалось загрузить список чатов. Попробуй позже.")
                .BackButton();
        }

        Result<IReadOnlyDictionary<Guid, PlanGrantDto>, Error> accessResult =
            await TelegramGrantChatAccessResolver.ResolveAsync(
                grantsResult.Value,
                _accessClient,
                ctx.CancellationToken);
        if (accessResult.IsFailure)
        {
            _logger.LogWarning(
                "Telegram chat access resolution failed for user {UserId}: {Code}",
                linkResult.Value.PlatformUserId,
                accessResult.Error.Messages[0].Code);
            return new ScreenView("Не удалось загрузить список чатов. Попробуй позже.").BackButton();
        }

        IReadOnlySet<Guid> activePlanIds = accessResult.Value.Keys.ToHashSet();

        if (activePlanIds.Count == 0)
        {
            return new ScreenView(
                    "Планов с привязанным чатом, на которые у тебя есть доступ, пока нет. " +
                    "Приобрети план, и invite-link придёт автоматически.")
                .BackButton();
        }

        // 2) Bindings для этих планов с EnrollmentGrantsMembership=true.
        IReadOnlyList<ChatBinding> accessibleBindings = await _bindings.GetManyByAsync(
            x => x.EnrollmentGrantsMembership && activePlanIds.Contains(x.PlanId),
            ctx.CancellationToken);

        if (accessibleBindings.Count == 0)
        {
            return new ScreenView(
                    "Планов с привязанным чатом, на которые у тебя есть доступ, пока нет. " +
                    "Приобрети план, и invite-link придёт автоматически.")
                .BackButton();
        }

        // 3) Дедуп по TelegramChatId (один чат может быть привязан к нескольким планам).
        List<ChatBinding> uniqueChats = accessibleBindings
            .GroupBy(b => b.TelegramChatId)
            .Select(g => g.First())
            .ToList();

        ScreenView view = new(
            $"Доступные чаты планов ({uniqueChats.Count}):\n\n" +
            "Жми на нужную кнопку — Telegram попросит подтвердить вступление, бот пустит автоматически.");

        foreach (ChatBinding chat in uniqueChats)
        {
            string title = string.IsNullOrEmpty(chat.ChatTitle) ? "Без названия" : chat.ChatTitle;
            view.UrlButton($"💬 {title}", chat.InviteLink).Row();
        }

        view.BackButton();
        return view;
    }
}
