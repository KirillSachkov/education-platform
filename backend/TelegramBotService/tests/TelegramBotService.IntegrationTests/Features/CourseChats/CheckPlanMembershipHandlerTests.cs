using NSubstitute;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Core.Features.CourseChats.UseCases;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// S2S membership-check (<c>GET /internal/telegram/users/{userId}/plans/{planId}/membership/</c>):
/// member / not_member / unknown в зависимости от UserLink + getChatMember.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class CheckPlanMembershipHandlerTests : TelegramBotTestsBase
{
    public CheckPlanMembershipHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Handle_NoUserLink_ReturnsUnknown()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3001;
        await SeedChatBindingAsync(planId, chatId);

        await using TelegramBotDbContext db = BuildDbContext();
        CheckPlanMembershipHandler handler = BuildHandler(db);

        var result = await handler.Handle(new CheckPlanMembershipQuery(platformUserId, planId), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsMember);
        Assert.Equal("unknown", result.Value.Status);
    }

    [Fact]
    public async Task Handle_LinkedUserIsMember_ReturnsMember()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3002;
        long telegramUserId = 700_002;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId);

        ChatApi.GetChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(new ChatMemberInfo(telegramUserId, ChatMembership.MEMBER)));

        await using TelegramBotDbContext db = BuildDbContext();
        CheckPlanMembershipHandler handler = BuildHandler(db);

        var result = await handler.Handle(new CheckPlanMembershipQuery(platformUserId, planId), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsMember);
        Assert.Equal("member", result.Value.Status);
    }

    [Fact]
    public async Task Handle_LinkedUserNotMember_ReturnsNotMember()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatId = -3003;
        long telegramUserId = 700_003;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatId);

        ChatApi.GetChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(new ChatMemberInfo(telegramUserId, ChatMembership.NOT_MEMBER)));

        await using TelegramBotDbContext db = BuildDbContext();
        CheckPlanMembershipHandler handler = BuildHandler(db);

        var result = await handler.Handle(new CheckPlanMembershipQuery(platformUserId, planId), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsMember);
        Assert.Equal("not_member", result.Value.Status);
    }

    [Fact]
    public async Task Handle_PlanHasNoBoundChats_ReturnsNotMember()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long telegramUserId = 700_004;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        // No chat binding seeded for this plan.

        await using TelegramBotDbContext db = BuildDbContext();
        CheckPlanMembershipHandler handler = BuildHandler(db);

        var result = await handler.Handle(new CheckPlanMembershipQuery(platformUserId, planId), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsMember);
        Assert.Equal("not_member", result.Value.Status);
    }

    [Fact]
    public async Task Handle_MemberInAnyOfMultipleChats_ReturnsMember()
    {
        Guid platformUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long chatA = -3005;
        long chatB = -3006;
        long telegramUserId = 700_005;

        await SeedUserLinkAsync(telegramUserId, platformUserId);
        await SeedChatBindingAsync(planId, chatA);
        await SeedChatBindingAsync(planId, chatB);

        // Not a member of A, member of B → overall member.
        ChatApi.GetChatMemberAsync(chatA, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(new ChatMemberInfo(telegramUserId, ChatMembership.NOT_MEMBER)));
        ChatApi.GetChatMemberAsync(chatB, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(new ChatMemberInfo(telegramUserId, ChatMembership.MEMBER)));

        await using TelegramBotDbContext db = BuildDbContext();
        CheckPlanMembershipHandler handler = BuildHandler(db);

        var result = await handler.Handle(new CheckPlanMembershipQuery(platformUserId, planId), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsMember);
        Assert.Equal("member", result.Value.Status);
    }

    private CheckPlanMembershipHandler BuildHandler(TelegramBotDbContext db) =>
        new(
            new ChatBindingRepository(db),
            BuildUserLinkRepository(db),
            BuildMembershipChecker());

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
}
