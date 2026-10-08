using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Messaging.Consumers;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.Consumers;

/// <summary>
/// F1: PlanGrantCreated → DM с invite link в каждый bound chat плана.
/// Покрывает: happy path, no bindings, no UserLink, soft-blocked link (regression #247),
/// no community cap (FREE LEARN_ONLY plan), already-member skip.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class PlanGrantCreatedTelegramHandlerTests : TelegramBotTestsBase
{
    private const string CAP_COMMUNITY_ACCESS = "COMMUNITY_ACCESS";

    private readonly IAccessServiceClient _accessClient;

    public PlanGrantCreatedTelegramHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _accessClient = Substitute.For<IAccessServiceClient>();

        // Дефолт — без welcome'а: member-branch не постит приветствие, пока тест не override'ит.
        _accessClient.GetPlanTelegramInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(ci.Arg<Guid>(), "Plan", "FULL_ALL", WelcomeMessage: null)));
    }

    [Fact]
    public async Task Handle_HappyPath_SendsInviteDmForEachBoundChat()
    {
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 700_001;

        await SeedUserLinkAsync(tgUserId, userId);
        await SeedChatBindingAsync(planId, telegramChatId: -1001, enrollmentGrantsMembership: true,
            inviteLink: "https://t.me/+abc", chatTitle: "Premium chat");

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildEvent(userId, planId), default);

        await BotNotifier.Received(1).SendTextAsync(
            tgUserId,
            Arg.Is<string>(s => s.Contains("Premium chat", StringComparison.Ordinal)),
            Arg.Is<InlineKeyboardMarkup?>(kb => kb != null
                && kb.InlineKeyboard.Single().Single().Url == "https://t.me/+abc"),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoBindingsForPlan_DoesNothing()
    {
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 700_002;

        await SeedUserLinkAsync(tgUserId, userId);
        // Никаких bindings.

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildEvent(userId, planId), default);

        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoUserLink_DoesNothing()
    {
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        await SeedChatBindingAsync(planId, telegramChatId: -1001, enrollmentGrantsMembership: true);

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildEvent(userId, planId), default);

        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SoftBlockedLink_DoesNotSendDm()
    {
        // Regression for #247: soft-blocked link must skip — иначе 403 от Telegram
        // приходит как warning, link blocked-state не обновляется, метрики врут.
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 700_003;

        await SeedUserLinkAsync(tgUserId, userId);
        await ExecuteInDb(async db =>
        {
            UserLink link = await db.UserLinks.SingleAsync(x => x.TelegramUserId == tgUserId);
            link.Block("bot_blocked");
            await db.SaveChangesAsync();
        });
        await SeedChatBindingAsync(planId, telegramChatId: -1001, enrollmentGrantsMembership: true);

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildEvent(userId, planId), default);

        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoCommunityAccessCapability_DoesNotSendDm()
    {
        // FREE LEARN_ONLY план не открывает чат — cap-check фильтрует.
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 700_004;

        await SeedUserLinkAsync(tgUserId, userId);
        await SeedChatBindingAsync(planId, telegramChatId: -1001, enrollmentGrantsMembership: true);

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildEvent(userId, planId, capabilities: []), default);

        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserAlreadyMember_SkipsInviteDm()
    {
        // ChatMembershipChecker возвращает Member → DM не нужен.
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 700_005;
        long chatId = -1001;

        await SeedUserLinkAsync(tgUserId, userId);
        await SeedChatBindingAsync(planId, telegramChatId: chatId, enrollmentGrantsMembership: true);

        // Configure ChatApi to return confirmed Member.
        ChatApi.GetChatMemberAsync(chatId, tgUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(
                new ChatMemberInfo(tgUserId, ChatMembership.MEMBER)));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildEvent(userId, planId), default);

        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserAlreadyMember_WithWelcomeConfigured_SendsWelcomeDm()
    {
        // Symptom #1 (#444): уже-участник на момент гранта ДОЛЖЕН получить приветствие плана.
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 700_006;
        long chatId = -1001;

        await SeedUserLinkAsync(tgUserId, userId);
        await SeedChatBindingAsync(planId, telegramChatId: chatId, enrollmentGrantsMembership: true);
        ChatApi.GetChatMemberAsync(chatId, tgUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(
                new ChatMemberInfo(tgUserId, ChatMembership.MEMBER)));
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(planId, "Plan", "FULL_ALL", WelcomeMessage: "Добро пожаловать в план!")));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildEvent(userId, planId), default);

        // DM с приветствием ушёл (invite уже-участнику не нужен).
        await BotNotifier.Received(1).SendTextAsync(
            tgUserId,
            Arg.Is<string>(s => s.Contains("Добро пожаловать в план", StringComparison.Ordinal)),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// #625 regression: trial FULL_ALL plan has no direct ChatBinding. F1 must use bindings
    /// from other FULL_ALL plans (the lifetime peer) and send the invite DM.
    /// </summary>
    [Fact]
    public async Task Handle_TrialFullAllGrant_NoDirectBindings_UsesLifetimePeerBindings()
    {
        Guid userId = Guid.NewGuid();
        Guid trialPlanId = Guid.NewGuid();     // plan user bought (trial)
        Guid lifetimePlanId = Guid.NewGuid(); // plan the chat is bound to
        long tgUserId = 700_010;
        long chatId = -1001;

        await SeedUserLinkAsync(tgUserId, userId);
        // No binding for trial plan — only lifetime plan has a binding.
        await SeedChatBindingAsync(lifetimePlanId, telegramChatId: chatId,
            enrollmentGrantsMembership: true, inviteLink: "https://t.me/+trial-peer",
            chatTitle: ".NET Fullstack");

        // AccessService resolves the trial to one canonical lifetime Telegram plan.
        _accessClient.GetPlanTelegramInfoAsync(trialPlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    trialPlanId,
                    "Пробный доступ",
                    "FULL_ALL",
                    WelcomeMessage: null,
                    CanonicalTelegramPlanId: lifetimePlanId)));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantCreatedTelegramHandler handler = BuildHandler(db);

        // Trial FULL_ALL grant: ExpiresAt is set (time-bounded), PlanTier = FULL_ALL.
        PlanGrantCreated evt = new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: trialPlanId,
            PlanTier: "FULL_ALL",
            PlanAuthorId: Guid.NewGuid(),
            CourseId: null,
            IncludesFutureContent: true,
            Source: "PURCHASE",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(30),
            Capabilities: ["VIEW_MATERIALS", "COMMUNITY_ACCESS"]);

        await handler.Handle(evt, default);

        // Invite DM must be sent via the lifetime plan's binding.
        await BotNotifier.Received(1).SendTextAsync(
            tgUserId,
            Arg.Is<string>(s => s.Contains(".NET Fullstack", StringComparison.Ordinal)),
            Arg.Is<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(kb => kb != null
                && kb.InlineKeyboard.Single().Single().Url == "https://t.me/+trial-peer"),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// #625 regression: non-trial (lifetime) FULL_ALL grant with no bindings must NOT
    /// fall through to peer-binding lookup (fallback is trial-only gate: ExpiresAt != null).
    /// </summary>
    [Fact]
    public async Task Handle_LifetimeFullAllGrant_NoDirectBindings_DoesNothing()
    {
        Guid userId = Guid.NewGuid();
        Guid lifetimePlanId = Guid.NewGuid();
        Guid otherPlanId = Guid.NewGuid();
        long tgUserId = 700_011;

        await SeedUserLinkAsync(tgUserId, userId);
        // Binding on a different plan — should not be picked up for non-trial.
        await SeedChatBindingAsync(otherPlanId, telegramChatId: -2000,
            enrollmentGrantsMembership: true);

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantCreatedTelegramHandler handler = BuildHandler(db);

        // Lifetime grant: ExpiresAt is null → no trial fallback.
        PlanGrantCreated evt = new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: lifetimePlanId,
            PlanTier: "FULL_ALL",
            PlanAuthorId: Guid.NewGuid(),
            CourseId: null,
            IncludesFutureContent: true,
            Source: "ADMIN_GRANT",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Capabilities: ["VIEW_MATERIALS", "COMMUNITY_ACCESS"]);

        await handler.Handle(evt, default);

        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TrialFullAllGrant_UsesOnlyCanonicalPeerBindings()
    {
        Guid userId = Guid.NewGuid();
        Guid trialPlanId = Guid.NewGuid();
        Guid canonicalPlanId = Guid.NewGuid();
        Guid unrelatedFullAllPlanId = Guid.NewGuid();
        long tgUserId = 700_012;

        await SeedUserLinkAsync(tgUserId, userId);
        await SeedChatBindingAsync(
            canonicalPlanId, -3001, true, "https://t.me/+canonical", "Canonical chat");
        await SeedChatBindingAsync(
            unrelatedFullAllPlanId, -3002, true, "https://t.me/+unrelated", "Unrelated chat");
        _accessClient.GetPlanTelegramInfoAsync(trialPlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    trialPlanId, "Trial", "FULL_ALL", null, canonicalPlanId)));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildEvent(userId, trialPlanId, expiresAt: DateTimeOffset.UtcNow.AddDays(7)), default);

        await BotNotifier.Received(1).SendTextAsync(
            tgUserId,
            Arg.Is<string>(text => text.Contains("Canonical chat", StringComparison.Ordinal)
                                   && !text.Contains("Unrelated chat", StringComparison.Ordinal)),
            Arg.Is<InlineKeyboardMarkup?>(keyboard => keyboard != null
                && keyboard.InlineKeyboard.Single().Single().Url == "https://t.me/+canonical"),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TrialFullAllGrant_PeerLookupFailure_ThrowsForRetry()
    {
        Guid userId = Guid.NewGuid();
        Guid trialPlanId = Guid.NewGuid();
        Guid peerPlanId = Guid.NewGuid();

        await SeedChatBindingAsync(peerPlanId, -4001, true);
        _accessClient.GetPlanTelegramInfoAsync(trialPlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PlanTelegramInfoDto, Error>(
                Error.Failure("access.unavailable", "AccessService unavailable")));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantCreatedTelegramHandler handler = BuildHandler(db);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            handler.Handle(BuildEvent(userId, trialPlanId, expiresAt: DateTimeOffset.UtcNow.AddDays(7)), default));
    }

    private PlanGrantCreatedTelegramHandler BuildHandler(TelegramBotDbContext db) =>
        new(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            _accessClient,
            BotNotifier,
            BuildPlanWelcomeService(_accessClient),
            BuildMembershipChecker(),
            NullLogger<PlanGrantCreatedTelegramHandler>.Instance);

    private async Task SeedUserLinkAsync(long telegramUserId, Guid platformUserId) =>
        await ExecuteInDb(async db =>
        {
            UserLink link = UserLink.Create(telegramUserId, platformUserId, "u").Value;
            await db.UserLinks.AddAsync(link);
            await db.SaveChangesAsync();
        });

    private async Task SeedChatBindingAsync(
        Guid planId, long telegramChatId, bool enrollmentGrantsMembership,
        string inviteLink = "https://t.me/+xyz", string chatTitle = "Plan chat") =>
        await ExecuteInDb(async db =>
        {
            ChatBinding b = ChatBinding.Create(
                Guid.NewGuid(), planId, telegramChatId, ChatType.SUPERGROUP,
                chatTitle, inviteLink,
                enrollmentGrantsMembership: enrollmentGrantsMembership,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });

    private static PlanGrantCreated BuildEvent(
        Guid userId,
        Guid planId,
        DateTimeOffset? expiresAt = null,
        IReadOnlyList<string>? capabilities = null) =>
        new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: planId,
            PlanTier: "FULL_ALL",
            PlanAuthorId: Guid.NewGuid(),
            CourseId: null,
            IncludesFutureContent: true,
            Source: "ADMIN_GRANT",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: expiresAt,
            Capabilities: capabilities ?? ["VIEW_MATERIALS", CAP_COMMUNITY_ACCESS]);
}
