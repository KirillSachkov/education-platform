using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlatformAuth.Middleware;
using SharedKernel;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Features.CourseChats.UseCases;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// <c>POST /telegram/admin/users/{userId}/plans/{planId}/welcome/resend/</c> (#444):
/// support-action «переотправить приветствие плана». Ключевой инвариант — <c>force:true</c>
/// минует dedup-стор: приветствие уходит даже когда стор уже содержит ключ (user, plan).
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class ResendPlanWelcomeHandlerTests : TelegramBotTestsBase
{
    private readonly IAccessServiceClient _accessClient;
    private readonly UserScopedData _admin;

    public ResendPlanWelcomeHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _accessClient = Substitute.For<IAccessServiceClient>();
        _admin = new UserScopedData();
        _admin.Authenticate(Guid.NewGuid(), "admin", "a@x.io", ["platform-admin"]);
    }

    [Fact]
    public async Task Handle_ForceBypassesDedup_SendsWelcomeEvenWhenAlreadySent()
    {
        Guid targetUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 810_001;
        long chatId = -8101;

        await SeedLinkedUserWithBoundChatAsync(targetUserId, tgUserId, planId, chatId);

        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(planId, "Plan X", "FULL_ALL", "Добро пожаловать!")));

        // Пред-засеваем dedup-стор для DM-направления (ResendPlanWelcome шлёт в личку):
        // без force приветствие было бы AlreadySent.
        InMemoryPlanWelcomeSentStore sentStore = new();
        await sentStore.TryMarkSentAsync(planId, tgUserId, WelcomeDestination.DirectMessage, default);

        await using TelegramBotDbContext db = BuildDbContext();
        ResendPlanWelcomeHandler handler = BuildHandler(db, sentStore);

        var result = await handler.Handle(new ResendPlanWelcomeCommand(targetUserId, planId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Sent", result.Value.Outcome);

        // Несмотря на pre-seeded стор — welcome реально ушёл в личку (force обошёл dedup).
        await BotNotifier.Received(1).SendTextAsync(
            tgUserId,
            Arg.Is<string>(s => s.Contains("Добро пожаловать", StringComparison.Ordinal)),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoTelegramLink_ReturnsNotLinked()
    {
        Guid targetUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();

        // Binding есть, а UserLink нет → NotLinked (резолв линка идёт первым).
        await SeedBoundChatAsync(planId, -8102);

        await using TelegramBotDbContext db = BuildDbContext();
        ResendPlanWelcomeHandler handler = BuildHandler(db, new InMemoryPlanWelcomeSentStore());

        var result = await handler.Handle(new ResendPlanWelcomeCommand(targetUserId, planId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("NotLinked", result.Value.Outcome);

        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            default, default!, default, default, default);
    }

    [Fact]
    public async Task Handle_NoBoundChat_ReturnsNoChatBound()
    {
        Guid targetUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 810_003;

        // Линк есть, чатов у плана нет → NoChatBound.
        await ExecuteInDb(async db =>
        {
            await db.UserLinks.AddAsync(UserLink.Create(tgUserId, targetUserId, "u").Value);
            await db.SaveChangesAsync();
        });

        await using TelegramBotDbContext db = BuildDbContext();
        ResendPlanWelcomeHandler handler = BuildHandler(db, new InMemoryPlanWelcomeSentStore());

        var result = await handler.Handle(new ResendPlanWelcomeCommand(targetUserId, planId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("NoChatBound", result.Value.Outcome);
    }

    private ResendPlanWelcomeHandler BuildHandler(TelegramBotDbContext db, IPlanWelcomeSentStore sentStore) =>
        new(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            BuildPlanWelcomeService(_accessClient, sentStore),
            _admin,
            NullLogger<ResendPlanWelcomeHandler>.Instance);

    private async Task SeedLinkedUserWithBoundChatAsync(
        Guid platformUserId, long tgUserId, Guid planId, long chatId)
    {
        await ExecuteInDb(async db =>
        {
            await db.UserLinks.AddAsync(UserLink.Create(tgUserId, platformUserId, "u").Value);
            await db.ChatBindings.AddAsync(NewBinding(planId, chatId));
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedBoundChatAsync(Guid planId, long chatId) =>
        await ExecuteInDb(async db =>
        {
            await db.ChatBindings.AddAsync(NewBinding(planId, chatId));
            await db.SaveChangesAsync();
        });

    private static ChatBinding NewBinding(Guid planId, long chatId) =>
        ChatBinding.Create(
            Guid.NewGuid(), planId, chatId, ChatType.SUPERGROUP,
            "Welcome chat", "https://t.me/+welcome",
            enrollmentGrantsMembership: true,
            membershipGrantsEnrollment: false,
            autoKickOnRevoke: false,
            enforceMembership: false,
            createdBy: Guid.NewGuid()).Value;
}
