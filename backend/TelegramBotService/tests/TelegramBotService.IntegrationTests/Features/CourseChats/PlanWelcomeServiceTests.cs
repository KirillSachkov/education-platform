using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using NSubstitute;
using SharedKernel;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.IntegrationTests.Infrastructure;
using InlineKeyboardMarkup = Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup;
using ParseMode = Telegram.Bot.Types.Enums.ParseMode;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
///     #687: destination-aware dedup + generic group-welcome fallback в <see cref="PlanWelcomeService"/>.
///     <list type="bullet">
///         <item>Раннее DM-приветствие НЕ подавляет позднее групповое (и наоборот) — ключ
///         dedup-стора теперь учитывает <see cref="WelcomeDestination"/>.</item>
///         <item>Дедуп per (plan, destination, user) по-прежнему гасит повтор того же направления.</item>
///         <item>Если автор не настроил приветствие — GROUP получает generic-фолбэк
///         (<see cref="PlanWelcomeService.DEFAULT_GROUP_WELCOME"/>), DM остаётся тихим.</item>
///     </list>
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class PlanWelcomeServiceTests : TelegramBotTestsBase
{
    private readonly IAccessServiceClient _accessClient;

    public PlanWelcomeServiceTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _accessClient = Substitute.For<IAccessServiceClient>();
    }

    [Fact]
    public async Task TrySendWelcome_GroupAfterDmSent_StillSendsGroupWelcome()
    {
        // Dest-aware dedup: DM welcome не должен подавить позднее групповое приветствие.
        Guid planId = Guid.NewGuid();
        long tgUserId = 900_001;
        long chatId = -9001;
        StubWelcome(planId, "Привет, добро пожаловать!");

        IPlanWelcomeSentStore sharedStore = new InMemoryPlanWelcomeSentStore();
        PlanWelcomeService svc = BuildPlanWelcomeService(_accessClient, sharedStore);

        WelcomeOutcome dm = await svc.TrySendWelcomeAsync(
            planId, tgUserId, chatId, WelcomeDestination.DirectMessage, default);
        WelcomeOutcome group = await svc.TrySendWelcomeAsync(
            planId, tgUserId, chatId, WelcomeDestination.Group, default);

        Assert.Equal(WelcomeOutcome.Sent, dm);
        Assert.Equal(WelcomeOutcome.Sent, group);

        // DM ушёл в личку юзеру, групповое — в чат.
        await BotNotifier.Received(1).SendTextAsync(
            tgUserId, Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
        await BotNotifier.Received(1).SendTextAsync(
            chatId, Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySendWelcome_SameDestinationTwice_SecondIsAlreadySent()
    {
        // Дедуп внутри одного направления по-прежнему работает.
        Guid planId = Guid.NewGuid();
        long tgUserId = 900_002;
        long chatId = -9002;
        StubWelcome(planId, "Привет!");

        IPlanWelcomeSentStore sharedStore = new InMemoryPlanWelcomeSentStore();
        PlanWelcomeService svc = BuildPlanWelcomeService(_accessClient, sharedStore);

        WelcomeOutcome first = await svc.TrySendWelcomeAsync(
            planId, tgUserId, chatId, WelcomeDestination.Group, default);
        WelcomeOutcome second = await svc.TrySendWelcomeAsync(
            planId, tgUserId, chatId, WelcomeDestination.Group, default);

        Assert.Equal(WelcomeOutcome.Sent, first);
        Assert.Equal(WelcomeOutcome.AlreadySent, second);
        await BotNotifier.Received(1).SendTextAsync(
            chatId, Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySendWelcome_ConcurrentSameKeyTriggers_SendsExactlyOnce()
    {
        // Прод-инцидент 2026-07-05: один вход в группу даёт ДВА Telegram-апдейта
        // (approve chat_join_request + chat_member join), оба конкурентно зовут
        // TrySendWelcomeAsync с одним ключом. Check-then-act окно (резолв текста через
        // AccessService + отправка) пропускало оба — юзер получал приветствие дважды.
        Guid planId = Guid.NewGuid();
        long tgUserId = 900_006;
        long chatId = -9006;

        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int resolveEntries = 0;
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (Interlocked.Increment(ref resolveEntries) == 1)
                    firstEntered.TrySetResult();
                else
                    secondEntered.TrySetResult();
                await release.Task;
                return Result.Success<PlanTelegramInfoDto, Error>(
                    new PlanTelegramInfoDto(planId, "Plan", "FULL_ALL", "Привет!"));
            });

        PlanWelcomeService svc = BuildPlanWelcomeService(_accessClient);

        Task<WelcomeOutcome> first = svc.TrySendWelcomeAsync(
            planId, tgUserId, chatId, WelcomeDestination.Group, default);
        await firstEntered.Task; // первый триггер занял dedup-ключ и завис в резолве текста

        Task<WelcomeOutcome> second = svc.TrySendWelcomeAsync(
            planId, tgUserId, chatId, WelcomeDestination.Group, default);
        // Второй обязан выйти AlreadySent, не дойдя до резолва; до фикса он тоже входил
        // в резолв (secondEntered) — ждём любой исход, затем отпускаем обе задачи.
        await Task.WhenAny(second, secondEntered.Task);
        release.TrySetResult();

        Assert.Equal(WelcomeOutcome.Sent, await first);
        Assert.Equal(WelcomeOutcome.AlreadySent, await second);
        await BotNotifier.Received(1).SendTextAsync(
            chatId, Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySendWelcome_GroupNoWelcomeConfigured_SendsGenericFallback()
    {
        // Автор не настроил приветствие → GROUP получает generic-фолбэк.
        Guid planId = Guid.NewGuid();
        long tgUserId = 900_003;
        long chatId = -9003;
        StubWelcome(planId, welcome: null);

        PlanWelcomeService svc = BuildPlanWelcomeService(_accessClient);

        WelcomeOutcome outcome = await svc.TrySendWelcomeAsync(
            planId, tgUserId, chatId, WelcomeDestination.Group, default);

        Assert.Equal(WelcomeOutcome.Sent, outcome);
        await BotNotifier.Received(1).SendTextAsync(
            chatId,
            Arg.Is<string>(s => s.Contains(PlanWelcomeService.DEFAULT_GROUP_WELCOME, StringComparison.Ordinal)),
            Arg.Any<InlineKeyboardMarkup?>(), Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySendWelcome_DmNoWelcomeConfigured_DoesNotSendGenericFallback()
    {
        // DM остаётся тихим — generic-фолбэк не спамит личку.
        Guid planId = Guid.NewGuid();
        long tgUserId = 900_004;
        long chatId = -9004;
        StubWelcome(planId, welcome: null);

        PlanWelcomeService svc = BuildPlanWelcomeService(_accessClient);

        WelcomeOutcome outcome = await svc.TrySendWelcomeAsync(
            planId, tgUserId, chatId, WelcomeDestination.DirectMessage, default);

        Assert.Equal(WelcomeOutcome.NoWelcomeConfigured, outcome);
        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySendWelcome_GroupInfoLookupFails_StillSendsGenericFallback()
    {
        // AccessService недоступен → GROUP всё равно встречает участника generic-фолбэком.
        Guid planId = Guid.NewGuid();
        long tgUserId = 900_005;
        long chatId = -9005;
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PlanTelegramInfoDto, Error>(GeneralErrors.ValueIsInvalid("plan")));

        PlanWelcomeService svc = BuildPlanWelcomeService(_accessClient);

        WelcomeOutcome outcome = await svc.TrySendWelcomeAsync(
            planId, tgUserId, chatId, WelcomeDestination.Group, default);

        Assert.Equal(WelcomeOutcome.Sent, outcome);
        await BotNotifier.Received(1).SendTextAsync(
            chatId,
            Arg.Is<string>(s => s.Contains(PlanWelcomeService.DEFAULT_GROUP_WELCOME, StringComparison.Ordinal)),
            Arg.Any<InlineKeyboardMarkup?>(), Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }

    private void StubWelcome(Guid planId, string? welcome) =>
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(planId, "Plan", "FULL_ALL", welcome)));
}
