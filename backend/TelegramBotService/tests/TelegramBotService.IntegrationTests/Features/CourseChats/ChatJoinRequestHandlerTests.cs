using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using SharedKernel;
using Telegram.Bot.Types;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Features.CourseChats.Handlers;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Options;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;
using UpdateContext = TelegramBotFlow.Core.Context.UpdateContext;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// F2: ChatJoinRequest → approve (если linked + active plan-grant)
/// или decline (no binding / no link / no enrollment) + DM CTA.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class ChatJoinRequestHandlerTests : TelegramBotTestsBase
{
    private readonly IAccessServiceClient _accessClient;
    private readonly IBotDecisionLogger _audit;

    public ChatJoinRequestHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _accessClient = Substitute.For<IAccessServiceClient>();
        _audit = Substitute.For<IBotDecisionLogger>();

        // #411 welcome-post (PostPlanWelcomeInGroupAsync) запрашивает telegram-info по плану.
        // Дефолт — Success без welcome'а: approve-path тесты не упираются в null-Result, а
        // welcome не постится (тесты welcome'а override'ят этот стаб своим .Returns).
        _accessClient.GetPlanTelegramInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    ci.Arg<Guid>(), "Test Plan", "FULL_ALL", null, ci.Arg<Guid>(),
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));
        ChatApi.ApproveChatJoinRequestAsync(
                Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Success(true));
        ChatApi.DeclineChatJoinRequestAsync(
                Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Success(true));
    }

    [Fact]
    public async Task Handle_NoChatBinding_DeclinesAndAudits()
    {
        long chatId = -2001;
        long telegramUserId = 600_001;

        await using TelegramBotDbContext db = BuildDbContext();
        ChatJoinRequestHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinRequestContext(chatId, telegramUserId, "user1"));

        await ChatApi.Received(1).DeclineChatJoinRequestAsync(chatId, telegramUserId, Arg.Any<CancellationToken>());
        await ChatApi.DidNotReceiveWithAnyArgs().ApproveChatJoinRequestAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(chatId, telegramUserId,
            Domain.Audit.BotDecisions.JOIN_REQUEST_DECLINED_NO_BINDING,
            reason: Arg.Any<string?>(),
            planId: Arg.Any<Guid?>(),
            ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoUserLink_DeclinesAndDmsLinkPrompt()
    {
        Guid planId = Guid.NewGuid();
        long chatId = -2002;
        long telegramUserId = 600_002;
        await SeedChatBindingAsync(planId, chatId);

        await using TelegramBotDbContext db = BuildDbContext();
        ChatJoinRequestHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinRequestContext(chatId, telegramUserId, "stranger"));

        await ChatApi.Received(1).DeclineChatJoinRequestAsync(chatId, telegramUserId, Arg.Any<CancellationToken>());
        // DM с просьбой привязать аккаунт — кнопка ведёт на РАБОЧУЮ страницу привязки
        // (/settings/integrations), а не на несуществующий /profile/integrations (prod-хотфикс 2026-06-02).
        await BotNotifier.Received(1).SendTextAsync(
            telegramUserId,
            Arg.Is<string>(s => s.Contains("привяжи аккаунт", StringComparison.Ordinal)),
            Arg.Is<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(m =>
                m != null && m.InlineKeyboard.SelectMany(row => row).Any(btn =>
                    string.Equals(btn.Url, "https://example.com/settings/integrations", StringComparison.Ordinal))),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(chatId, telegramUserId,
            Domain.Audit.BotDecisions.JOIN_REQUEST_DECLINED_NO_LINK,
            reason: Arg.Any<string?>(),
            planId: Arg.Any<Guid?>(),
            ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LinkedUserWithActiveGrant_Approves()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -2003;
        long telegramUserId = 600_003;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId);

        _accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([ActiveGrant(planId, platformUserId)]));

        await using TelegramBotDbContext db = BuildDbContext();
        ChatJoinRequestHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinRequestContext(chatId, telegramUserId, "linked"));

        await ChatApi.Received(1).ApproveChatJoinRequestAsync(chatId, telegramUserId, Arg.Any<CancellationToken>());
        await ChatApi.DidNotReceiveWithAnyArgs().DeclineChatJoinRequestAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(chatId, telegramUserId,
            Domain.Audit.BotDecisions.JOIN_REQUEST_APPROVED,
            reason: Arg.Any<string?>(),
            planId: planId,
            ct: Arg.Any<CancellationToken>());

        // L2: approve публикует ChatMemberConfirmed для matched плана.
        ChatMemberConfirmed ev = OutboxCollector.OfType<ChatMemberConfirmed>().Single();
        Assert.Equal(platformUserId, ev.PlatformUserId);
        Assert.Equal(planId, ev.PlanId);
        Assert.Equal(chatId, ev.TelegramChatId);
    }

    [Fact]
    public async Task Handle_ApproveApiFailure_DoesNotPublishOrRecordSuccess()
    {
        Guid platformUserId = Guid.CreateVersion7();
        Guid planId = Guid.CreateVersion7();
        long chatId = -2013;
        long telegramUserId = 600_013;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId);
        _accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([
                ActiveGrant(planId, platformUserId)
            ]));
        ChatApi.ApproveChatJoinRequestAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Failure(
                ChatApiErrorCode.ChatNotReachable,
                "bot is not an administrator"));

        await using TelegramBotDbContext db = BuildDbContext();
        ChatJoinRequestHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinRequestContext(chatId, telegramUserId, "approve_failed"));

        Assert.Empty(OutboxCollector.OfType<ChatMemberConfirmed>());
        await _audit.DidNotReceive().LogAsync(
            chatId,
            telegramUserId,
            Domain.Audit.BotDecisions.JOIN_REQUEST_APPROVED,
            reason: Arg.Any<string?>(),
            planId: Arg.Any<Guid?>(),
            ct: Arg.Any<CancellationToken>());
        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ChatBoundToTwoPlansBothGranted_ApprovesAndPublishesPerPlan()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planA = Guid.NewGuid();
        Guid planB = Guid.NewGuid();
        long chatId = -2005;
        long telegramUserId = 600_005;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planA, chatId);
        await SeedChatBindingAsync(planB, chatId);

        _accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([
                ActiveGrant(planA, platformUserId),
                ActiveGrant(planB, platformUserId),
            ]));

        await using TelegramBotDbContext db = BuildDbContext();
        ChatJoinRequestHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinRequestContext(chatId, telegramUserId, "dual"));

        await ChatApi.Received(1).ApproveChatJoinRequestAsync(chatId, telegramUserId, Arg.Any<CancellationToken>());

        // Один event на каждый matched план.
        IReadOnlyList<ChatMemberConfirmed> events = OutboxCollector.OfType<ChatMemberConfirmed>().ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(platformUserId, e.PlatformUserId));
        Assert.All(events, e => Assert.Equal(chatId, e.TelegramChatId));
        Assert.Contains(events, e => e.PlanId == planA);
        Assert.Contains(events, e => e.PlanId == planB);
    }

    [Fact]
    public async Task Handle_LinkedUserWithNoMatchingGrant_DeclinesAndSendsEnrollCta()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid lifetimePlanId = Guid.NewGuid();
        Guid unrelatedPlanId = Guid.NewGuid();
        long chatId = -2004;
        long telegramUserId = 600_004;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(lifetimePlanId, chatId);

        // У юзера есть grant на ДРУГОЙ FULL_ALL план. Совпадение tier не даёт права
        // входа: только exact/canonical PlanId может пересечь bound планы.
        _accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([
                ActiveGrant(unrelatedPlanId, platformUserId)
            ]));
        _accessClient.GetPlanTelegramInfoAsync(unrelatedPlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    unrelatedPlanId, "Другой полный доступ", "FULL_ALL", null, unrelatedPlanId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

        await using TelegramBotDbContext db = BuildDbContext();
        ChatJoinRequestHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinRequestContext(chatId, telegramUserId, "registered"));

        await ChatApi.Received(1).DeclineChatJoinRequestAsync(chatId, telegramUserId, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(chatId, telegramUserId,
            Domain.Audit.BotDecisions.JOIN_REQUEST_DECLINED_NO_ENROLLMENT,
            reason: Arg.Any<string?>(),
            planId: Arg.Any<Guid?>(),
            ct: Arg.Any<CancellationToken>());

        // Decline не публикует ChatMemberConfirmed.
        Assert.Empty(OutboxCollector.OfType<ChatMemberConfirmed>());
    }

    [Fact]
    public async Task Handle_ApproveWithWelcomeConfigured_PostsWelcomeInGroup()
    {
        // #411/#444: на approve join-request'а с настроенным welcome'ом бот постит его В ГРУППУ.
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -2006;
        long telegramUserId = 600_006;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId);
        _accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([ActiveGrant(planId, platformUserId)]));
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    planId, "Plan", "FULL_ALL", "Привет, добро пожаловать!", planId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

        await using TelegramBotDbContext db = BuildDbContext();
        ChatJoinRequestHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinRequestContext(chatId, telegramUserId, "welcomeuser"));

        await ChatApi.Received(1).ApproveChatJoinRequestAsync(chatId, telegramUserId, Arg.Any<CancellationToken>());
        await BotNotifier.Received(1).SendTextAsync(
            chatId,
            Arg.Is<string>(s => s.Contains("добро пожаловать", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActiveGrantWithoutCommunityCapability_Declines()
    {
        Guid platformUserId = Guid.CreateVersion7();
        Guid planId = Guid.CreateVersion7();
        long chatId = -2012;
        long telegramUserId = 600_012;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId);
        _accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([
                ActiveGrant(planId, platformUserId)
            ]));
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    planId, "Learn only", "COURSE", null, planId, ["VIEW_MATERIALS"])));

        await using TelegramBotDbContext db = BuildDbContext();
        ChatJoinRequestHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinRequestContext(chatId, telegramUserId, "learn_only"));

        await ChatApi.Received(1).DeclineChatJoinRequestAsync(
            chatId, telegramUserId, Arg.Any<CancellationToken>());
        await ChatApi.DidNotReceiveWithAnyArgs().ApproveChatJoinRequestAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    private ChatJoinRequestHandler BuildHandler(TelegramBotDbContext db) =>
        new(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            _accessClient,
            ChatApi,
            BotNotifier,
            BuildPlanWelcomeService(_accessClient),
            _audit,
            BuildOutboxService(),
            BuildTransactionManager(db),
            Options.Create(new TelegramNotificationOptions { FrontendBaseUrl = "https://example.com" }),
            NullLogger<ChatJoinRequestHandler>.Instance);

    private async Task SeedUserLinkAsync(long telegramUserId, Guid platformUserId) =>
        await ExecuteInDb(async db =>
        {
            UserLink link = UserLink.Create(telegramUserId, platformUserId, "u").Value;
            await db.UserLinks.AddAsync(link);
            await db.SaveChangesAsync();
        });

    private async Task SeedChatBindingAsync(Guid planId, long telegramChatId) =>
        await ExecuteInDb(async db =>
        {
            ChatBinding b = ChatBinding.Create(
                Guid.NewGuid(), planId, telegramChatId, ChatType.SUPERGROUP,
                "Plan chat", "https://t.me/+abc",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });

    private static UpdateContext BuildJoinRequestContext(long chatId, long telegramUserId, string? username)
    {
        ChatJoinRequest request = new()
        {
            Chat = new Chat { Id = chatId, Type = Telegram.Bot.Types.Enums.ChatType.Supergroup },
            From = new User { Id = telegramUserId, FirstName = "T", Username = username },
            Date = DateTime.UtcNow,
            UserChatId = telegramUserId,
        };

        Update update = new() { ChatJoinRequest = request };
        return new UpdateContext(update, Substitute.For<IServiceProvider>(), CancellationToken.None);
    }

    /// <summary>
    /// #625 regression: user holds active grant on trial FULL_ALL plan (different PlanId than
    /// the chat's bound lifetime plan). F2 must approve through the trial's canonical PlanId.
    /// </summary>
    [Fact]
    public async Task Handle_TrialFullAllGrant_ChatBoundToLifetimePlan_Approves()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid trialPlanId = Guid.NewGuid();     // user's grant plan (trial, different Id)
        Guid lifetimePlanId = Guid.NewGuid(); // plan the chat is bound to
        long chatId = -2010;
        long telegramUserId = 600_010;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(lifetimePlanId, chatId);

        // User holds an ACTIVE grant on the TRIAL plan — not the lifetime plan.
        _accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>(
                [ActiveGrant(trialPlanId, platformUserId)]));

        // AccessService maps the trial to the exact canonical lifetime plan.
        _accessClient.GetPlanTelegramInfoAsync(lifetimePlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    lifetimePlanId, "Полный доступ", "FULL_ALL", null, lifetimePlanId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));
        _accessClient.GetPlanTelegramInfoAsync(trialPlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    trialPlanId, "Пробный месяц", "FULL_ALL", null, lifetimePlanId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

        await using TelegramBotDbContext db = BuildDbContext();
        ChatJoinRequestHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinRequestContext(chatId, telegramUserId, "trial_user"));

        // Must be APPROVED because the trial resolves to this exact lifetime plan.
        await ChatApi.Received(1).ApproveChatJoinRequestAsync(chatId, telegramUserId, Arg.Any<CancellationToken>());
        await ChatApi.DidNotReceiveWithAnyArgs().DeclineChatJoinRequestAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());

        // ChatMemberConfirmed published for the bound FULL_ALL plan (lifetime).
        ChatMemberConfirmed confirmed = Assert.Single(OutboxCollector.OfType<ChatMemberConfirmed>());
        Assert.Equal(platformUserId, confirmed.PlatformUserId);
        Assert.Equal(lifetimePlanId, confirmed.PlanId);
        Assert.Equal(chatId, confirmed.TelegramChatId);
    }

    /// <summary>
    /// #625 regression: user holds only a COURSE-tier grant. Chat is bound to FULL_ALL plan.
    /// Canonical PlanId matching must NOT approve an unrelated course plan.
    /// </summary>
    [Fact]
    public async Task Handle_CourseGrantOnly_ChatBoundToFullAllPlan_Declines()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid coursePlanId = Guid.NewGuid();
        Guid lifetimePlanId = Guid.NewGuid();
        long chatId = -2011;
        long telegramUserId = 600_011;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(lifetimePlanId, chatId);

        _accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>(
                [ActiveGrant(coursePlanId, platformUserId)]));

        // The course plan resolves only to itself, not to the bound lifetime plan.
        _accessClient.GetPlanTelegramInfoAsync(lifetimePlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    lifetimePlanId, "Полный доступ", "FULL_ALL", null, lifetimePlanId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));
        _accessClient.GetPlanTelegramInfoAsync(coursePlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    coursePlanId, "Курс", "COURSE", null, coursePlanId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

        await using TelegramBotDbContext db = BuildDbContext();
        ChatJoinRequestHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinRequestContext(chatId, telegramUserId, "course_only"));

        await ChatApi.Received(1).DeclineChatJoinRequestAsync(chatId, telegramUserId, Arg.Any<CancellationToken>());
        await ChatApi.DidNotReceiveWithAnyArgs().ApproveChatJoinRequestAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        Assert.Empty(OutboxCollector.OfType<ChatMemberConfirmed>());
    }

    private static PlanGrantDto ActiveGrant(Guid planId, Guid userId) =>
        new(
            Id: Guid.NewGuid(),
            UserId: userId,
            PlanId: planId,
            Source: "ADMIN_GRANT",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Status: "ACTIVE",
            RevokedAt: null,
            RevokeReason: null);
}
