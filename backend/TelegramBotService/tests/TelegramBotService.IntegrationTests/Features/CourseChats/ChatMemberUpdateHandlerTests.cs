using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using SharedKernel;
using Telegram.Bot.Types;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Features.CourseChats.Handlers;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;
using ChatMemberStatus = Telegram.Bot.Types.Enums.ChatMemberStatus;
using TgChatMember = Telegram.Bot.Types.ChatMember;
using UpdateContext = TelegramBotFlow.Core.Context.UpdateContext;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
///     ST-1 (#616): на genuine NEW join через <c>chat_member</c> (юзера добавил админ / авто-аппрув
///     Telegram, минуя <see cref="ChatJoinRequestHandler"/>) бот постит welcome В ГРУППУ и публикует
///     <see cref="ChatMemberConfirmed"/> — независимо от <c>EnforceMembership</c>. F5 enforcement (kick)
///     остаётся в силе для enforce-чатов без grant'а.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class ChatMemberUpdateHandlerTests : TelegramBotTestsBase
{
    private readonly IAccessServiceClient _accessClient;

    public ChatMemberUpdateHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _accessClient = Substitute.For<IAccessServiceClient>();

        // Дефолт — telegram-info Success без welcome'а: approve-path тесты не упираются в null-Result.
        _accessClient.GetPlanTelegramInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    ci.Arg<Guid>(), "Test Plan", "FULL_ALL", null, ci.Arg<Guid>(),
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));
    }

    [Fact]
    public async Task Handle_NewJoin_LinkedWithActiveGrant_PostsWelcomeAndPublishesConfirmed()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3001;
        long telegramUserId = 700_001;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        // EnforceMembership=false — welcome/event обязаны сработать всё равно.
        await SeedChatBindingAsync(planId, chatId, enforceMembership: false);
        StubActiveGrant(platformUserId, planId);
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    planId, "Plan", "FULL_ALL", "Привет, добро пожаловать!", planId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

        await using TelegramBotDbContext db = BuildDbContext();
        ChatMemberUpdateHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinContext(chatId, telegramUserId,
            oldStatus: ChatMemberStatus.Left, newStatus: ChatMemberStatus.Member));

        // Welcome запостен В ГРУППУ.
        await BotNotifier.Received(1).SendTextAsync(
            chatId,
            Arg.Is<string>(s => s.Contains("добро пожаловать", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());

        // ChatMemberConfirmed опубликован для matched плана.
        ChatMemberConfirmed ev = OutboxCollector.OfType<ChatMemberConfirmed>().Single();
        Assert.Equal(platformUserId, ev.PlatformUserId);
        Assert.Equal(planId, ev.PlanId);
        Assert.Equal(chatId, ev.TelegramChatId);

        // EnforceMembership=false → kick не вызывается.
        await ChatApi.DidNotReceiveWithAnyArgs().KickChatMemberAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SecondIdenticalJoin_DoesNotPostSecondWelcome()
    {
        // Dedup: повторный genuine-join того же юзера на тот же план не дублирует welcome.
        // Dedup-store shared между вызовами — передаём один и тот же sentStore в оба handler'а.
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3002;
        long telegramUserId = 700_002;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId, enforceMembership: false);
        StubActiveGrant(platformUserId, planId);
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    planId, "Plan", "FULL_ALL", "Добро пожаловать!", planId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

        IPlanWelcomeSentStore sharedStore = new InMemoryPlanWelcomeSentStore();

        await using (TelegramBotDbContext db1 = BuildDbContext())
        {
            ChatMemberUpdateHandler handler1 = BuildHandler(db1, sharedStore);
            await handler1.HandleAsync(BuildJoinContext(chatId, telegramUserId,
                ChatMemberStatus.Left, ChatMemberStatus.Member));
        }

        await using (TelegramBotDbContext db2 = BuildDbContext())
        {
            ChatMemberUpdateHandler handler2 = BuildHandler(db2, sharedStore);
            await handler2.HandleAsync(BuildJoinContext(chatId, telegramUserId,
                ChatMemberStatus.Left, ChatMemberStatus.Member));
        }

        // Только ОДИН welcome за два join'а.
        await BotNotifier.Received(1).SendTextAsync(
            chatId,
            Arg.Any<string>(),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NewJoin_NoMatchingGrant_NoWelcomeNoEvent()
    {
        // Юзер привязан, но grant на ДРУГОЙ план. Welcome/event не идут.
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3003;
        long telegramUserId = 700_003;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId, enforceMembership: false);
        StubActiveGrant(platformUserId, Guid.NewGuid()); // другой план

        await using TelegramBotDbContext db = BuildDbContext();
        ChatMemberUpdateHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinContext(chatId, telegramUserId,
            ChatMemberStatus.Left, ChatMemberStatus.Member));

        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(OutboxCollector.OfType<ChatMemberConfirmed>());
        // EnforceMembership=false → существующее поведение (без kick) неизменно.
        await ChatApi.DidNotReceiveWithAnyArgs().KickChatMemberAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LeaveTransition_PublishesNothing()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3004;
        long telegramUserId = 700_004;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId, enforceMembership: false);
        StubActiveGrant(platformUserId, planId);

        await using TelegramBotDbContext db = BuildDbContext();
        ChatMemberUpdateHandler handler = BuildHandler(db);

        // member → left (выход).
        await handler.HandleAsync(BuildJoinContext(chatId, telegramUserId,
            ChatMemberStatus.Member, ChatMemberStatus.Left));

        Assert.Empty(OutboxCollector.OfType<ChatMemberConfirmed>());
        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
        await ChatApi.DidNotReceiveWithAnyArgs().KickChatMemberAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_KickTransition_PublishesNothing()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3005;
        long telegramUserId = 700_005;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId, enforceMembership: true);
        StubActiveGrant(platformUserId, planId);

        await using TelegramBotDbContext db = BuildDbContext();
        ChatMemberUpdateHandler handler = BuildHandler(db);

        // member → kicked.
        await handler.HandleAsync(BuildJoinContext(chatId, telegramUserId,
            ChatMemberStatus.Member, ChatMemberStatus.Kicked));

        Assert.Empty(OutboxCollector.OfType<ChatMemberConfirmed>());
        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
        await ChatApi.DidNotReceiveWithAnyArgs().KickChatMemberAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NewJoin_EnforceMembershipNoGrant_KicksAndStillNoWelcome()
    {
        // F5 enforcement intact: enforce-чат, нет grant'а → kick. Welcome/event не идут.
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3006;
        long telegramUserId = 700_006;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId, enforceMembership: true);
        StubActiveGrant(platformUserId, Guid.NewGuid()); // grant на чужой план

        ChatApi.KickChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Success(true));

        await using TelegramBotDbContext db = BuildDbContext();
        ChatMemberUpdateHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinContext(chatId, telegramUserId,
            ChatMemberStatus.Left, ChatMemberStatus.Member));

        await ChatApi.Received(1).KickChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>());
        Assert.Empty(OutboxCollector.OfType<ChatMemberConfirmed>());
        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NewJoin_NoUserLink_EnforceKicks_NoWelcome()
    {
        // Не привязан к платформе → нет grant'ов: enforce-чат кикает, welcome/event не идут.
        Guid planId = Guid.NewGuid();
        long chatId = -3007;
        long telegramUserId = 700_007;

        await SeedChatBindingAsync(planId, chatId, enforceMembership: true);

        ChatApi.KickChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Success(true));

        await using TelegramBotDbContext db = BuildDbContext();
        ChatMemberUpdateHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildJoinContext(chatId, telegramUserId,
            ChatMemberStatus.Left, ChatMemberStatus.Member));

        await ChatApi.Received(1).KickChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>());
        Assert.Empty(OutboxCollector.OfType<ChatMemberConfirmed>());
        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    private ChatMemberUpdateHandler BuildHandler(TelegramBotDbContext db) =>
        BuildHandler(db, new InMemoryPlanWelcomeSentStore());

    private ChatMemberUpdateHandler BuildHandler(TelegramBotDbContext db, IPlanWelcomeSentStore sentStore) =>
        new(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            _accessClient,
            ChatApi,
            BuildPlanWelcomeService(_accessClient, sentStore),
            BuildOutboxService(),
            BuildTransactionManager(db),
            NullLogger<ChatMemberUpdateHandler>.Instance);

    private void StubActiveGrant(Guid platformUserId, Guid planId) =>
        _accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([ActiveGrant(planId, platformUserId)]));

    private async Task SeedUserLinkAsync(long telegramUserId, Guid platformUserId) =>
        await ExecuteInDb(async db =>
        {
            UserLink link = UserLink.Create(telegramUserId, platformUserId, "u").Value;
            await db.UserLinks.AddAsync(link);
            await db.SaveChangesAsync();
        });

    private async Task SeedChatBindingAsync(Guid planId, long telegramChatId, bool enforceMembership) =>
        await ExecuteInDb(async db =>
        {
            ChatBinding b = ChatBinding.Create(
                Guid.NewGuid(), planId, telegramChatId, ChatType.SUPERGROUP,
                "Plan chat", "https://t.me/+abc",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: enforceMembership,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });

    private static UpdateContext BuildJoinContext(
        long chatId, long telegramUserId, ChatMemberStatus oldStatus, ChatMemberStatus newStatus)
    {
        User user = new() { Id = telegramUserId, FirstName = "T", Username = "joiner" };

        ChatMemberUpdated update = new()
        {
            Chat = new Chat { Id = chatId, Type = Telegram.Bot.Types.Enums.ChatType.Supergroup },
            From = new User { Id = 999, FirstName = "Admin" },
            Date = DateTime.UtcNow,
            OldChatMember = BuildChatMember(oldStatus, user),
            NewChatMember = BuildChatMember(newStatus, user),
        };

        Update tgUpdate = new() { ChatMember = update };
        return new UpdateContext(tgUpdate, Substitute.For<IServiceProvider>(), CancellationToken.None);
    }

    private static TgChatMember BuildChatMember(ChatMemberStatus status, User user) =>
        status switch
        {
            ChatMemberStatus.Member => new ChatMemberMember { User = user },
            ChatMemberStatus.Administrator => new ChatMemberAdministrator { User = user },
            ChatMemberStatus.Creator => new ChatMemberOwner { User = user },
            ChatMemberStatus.Left => new ChatMemberLeft { User = user },
            ChatMemberStatus.Kicked => new ChatMemberBanned { User = user },
            _ => new ChatMemberLeft { User = user },
        };

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
