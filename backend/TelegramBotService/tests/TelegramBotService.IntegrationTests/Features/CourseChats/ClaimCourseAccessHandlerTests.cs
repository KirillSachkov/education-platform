using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
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
/// F3 reverse-флоу: <c>/start claim_chat_&lt;chatId&gt;</c> — юзер уже в чате, бот
/// проверяет членство через getChatMember и выдаёт plan-grant если у chat'а есть
/// MembershipGrantsEnrollment binding.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class ClaimCourseAccessHandlerTests : TelegramBotTestsBase
{
    private readonly IAccessServiceClient _accessClient;
    private readonly IBotDecisionLogger _audit;

    public ClaimCourseAccessHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _accessClient = Substitute.For<IAccessServiceClient>();
        _audit = Substitute.For<IBotDecisionLogger>();
    }

    [Fact]
    public async Task Handle_UserNotMember_DeclinesWithAuditAndDoesNotGrant()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3001;
        long telegramUserId = 650_001;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId, membershipGrantsEnrollment: true);

        // Юзер не в чате.
        ChatApi.GetChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(
                new ChatMemberInfo(telegramUserId, ChatMembership.NOT_MEMBER)));

        await using TelegramBotDbContext db = BuildDbContext();
        ClaimCourseAccessHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildContext(telegramUserId, $"/start claim_chat_{chatId}"));

        await _accessClient.DidNotReceiveWithAnyArgs().GrantByPlanAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(chatId, telegramUserId,
            Domain.Audit.BotDecisions.CLAIM_DECLINED_NOT_MEMBER,
            reason: Arg.Any<string?>(),
            planId: Arg.Any<Guid?>(),
            ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserMemberWithMembershipGrantsEnrollment_GrantsPlanAccess()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3002;
        long telegramUserId = 650_002;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId, membershipGrantsEnrollment: true);

        ChatApi.GetChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(
                new ChatMemberInfo(telegramUserId, ChatMembership.MEMBER)));

        _accessClient.GrantByPlanAsync(platformUserId, planId, "TELEGRAM_F1", null, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanGrantDto, Error>(FreshGrant(planId, platformUserId)));

        await using TelegramBotDbContext db = BuildDbContext();
        ClaimCourseAccessHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildContext(telegramUserId, $"/start claim_chat_{chatId}"));

        await _accessClient.Received(1).GrantByPlanAsync(
            platformUserId, planId, "TELEGRAM_F1", null, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(chatId, telegramUserId,
            Domain.Audit.BotDecisions.CLAIM_GRANTED,
            reason: Arg.Any<string?>(),
            planId: planId,
            ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserMemberButChatHasNoMembershipGrantsEnrollment_AuditsNoBinding()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3003;
        long telegramUserId = 650_003;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        // Binding есть, но MembershipGrantsEnrollment=false → не выдаём.
        await SeedChatBindingAsync(planId, chatId, membershipGrantsEnrollment: false);

        ChatApi.GetChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(
                new ChatMemberInfo(telegramUserId, ChatMembership.MEMBER)));

        await using TelegramBotDbContext db = BuildDbContext();
        ClaimCourseAccessHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildContext(telegramUserId, $"/start claim_chat_{chatId}"));

        await _accessClient.DidNotReceiveWithAnyArgs().GrantByPlanAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(chatId, telegramUserId,
            Domain.Audit.BotDecisions.CLAIM_DECLINED_NO_BINDING,
            reason: Arg.Any<string?>(),
            planId: Arg.Any<Guid?>(),
            ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ChannelMemberWithMembershipGrantsEnrollment_GrantsPlanAccess()
    {
        // Закрытый канал (#416): membership проверяется через getChatMember(channelId, userId) —
        // надёжно когда бот админ канала. Member/administrator/creator считаются участием.
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -100_500_001;
        long telegramUserId = 650_010;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId, membershipGrantsEnrollment: true, chatType: ChatType.CHANNEL);

        ChatApi.GetChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(
                new ChatMemberInfo(telegramUserId, ChatMembership.MEMBER)));

        _accessClient.GrantByPlanAsync(platformUserId, planId, "TELEGRAM_F1", null, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanGrantDto, Error>(FreshGrant(planId, platformUserId)));

        await using TelegramBotDbContext db = BuildDbContext();
        ClaimCourseAccessHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildContext(telegramUserId, $"/start claim_chat_{chatId}"));

        await _accessClient.Received(1).GrantByPlanAsync(
            platformUserId, planId, "TELEGRAM_F1", null, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(chatId, telegramUserId,
            Domain.Audit.BotDecisions.CLAIM_GRANTED,
            reason: Arg.Any<string?>(),
            planId: planId,
            ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ChannelNonSubscriber_DeclinesAndDoesNotGrant()
    {
        // Не подписан на закрытый канал → getChatMember=NOT_MEMBER → отказ, никакого grant'а.
        // Защита от ложных grants: без подтверждённого членства доступ не выдаётся.
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -100_500_002;
        long telegramUserId = 650_011;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId, membershipGrantsEnrollment: true, chatType: ChatType.CHANNEL);

        ChatApi.GetChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(
                new ChatMemberInfo(telegramUserId, ChatMembership.NOT_MEMBER)));

        await using TelegramBotDbContext db = BuildDbContext();
        ClaimCourseAccessHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildContext(telegramUserId, $"/start claim_chat_{chatId}"));

        await _accessClient.DidNotReceiveWithAnyArgs().GrantByPlanAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(chatId, telegramUserId,
            Domain.Audit.BotDecisions.CLAIM_DECLINED_NOT_MEMBER,
            reason: Arg.Any<string?>(),
            planId: Arg.Any<Guid?>(),
            ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoUserLink_RequestsAccountLinking()
    {
        Guid planId = Guid.NewGuid();
        long chatId = -3004;
        long telegramUserId = 650_004;
        await SeedChatBindingAsync(planId, chatId, membershipGrantsEnrollment: true);

        await using TelegramBotDbContext db = BuildDbContext();
        ClaimCourseAccessHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildContext(telegramUserId, $"/start claim_chat_{chatId}"));

        // Никаких HTTP вызовов в AccessService.
        await _accessClient.DidNotReceiveWithAnyArgs().GrantByPlanAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        // DM с просьбой привязать аккаунт — кнопка должна вести на РАБОЧУЮ страницу привязки
        // (/settings/integrations), а не на несуществующий /profile/integrations (404 в Telegram
        // in-app browser ломал весь account-attach флоу — prod-хотфикс 2026-06-02).
        await BotNotifier.Received(1).SendTextAsync(
            telegramUserId,
            Arg.Is<string>(s => s.Contains("привяжи", StringComparison.Ordinal)),
            Arg.Is<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(m =>
                m != null && m.InlineKeyboard.SelectMany(row => row).Any(btn =>
                    string.Equals(btn.Url, "https://example.com/settings/integrations", StringComparison.Ordinal))),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    private ClaimCourseAccessHandler BuildHandler(TelegramBotDbContext db) =>
        new(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            ChatApi,
            _accessClient,
            BotNotifier,
            _audit,
            Options.Create(new TelegramNotificationOptions { FrontendBaseUrl = "https://example.com" }),
            NullLogger<ClaimCourseAccessHandler>.Instance);

    private async Task SeedUserLinkAsync(long telegramUserId, Guid platformUserId) =>
        await ExecuteInDb(async db =>
        {
            UserLink link = UserLink.Create(telegramUserId, platformUserId, "u").Value;
            await db.UserLinks.AddAsync(link);
            await db.SaveChangesAsync();
        });

    private async Task SeedChatBindingAsync(
        Guid planId, long telegramChatId, bool membershipGrantsEnrollment,
        ChatType chatType = ChatType.SUPERGROUP) =>
        await ExecuteInDb(async db =>
        {
            ChatBinding b = ChatBinding.Create(
                Guid.NewGuid(), planId, telegramChatId, chatType,
                "Plan chat", "https://t.me/+abc",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: membershipGrantsEnrollment,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });

    private static UpdateContext BuildContext(long telegramUserId, string messageText)
    {
        Update update = new()
        {
            Message = new Message
            {
                Id = 1,
                Text = messageText,
                Date = DateTime.UtcNow,
                From = new User { Id = telegramUserId, FirstName = "T" },
                Chat = new Chat { Id = telegramUserId, Type = Telegram.Bot.Types.Enums.ChatType.Private }
            }
        };
        return new UpdateContext(update, Substitute.For<IServiceProvider>(), CancellationToken.None);
    }

    private static PlanGrantDto FreshGrant(Guid planId, Guid userId) =>
        new(
            Id: Guid.NewGuid(),
            UserId: userId,
            PlanId: planId,
            Source: "TELEGRAM_F1",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Status: "ACTIVE",
            RevokedAt: null,
            RevokeReason: null);
}
