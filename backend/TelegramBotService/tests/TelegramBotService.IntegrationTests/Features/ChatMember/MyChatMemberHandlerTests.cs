using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using TelegramBotService.Core.Features.ChatMember.Handlers;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;
using UpdateContext = TelegramBotFlow.Core.Context.UpdateContext;

namespace TelegramBotService.IntegrationTests.Features.ChatMember;

/// <summary>
/// Регрессия #434: когда бота добавляют/повышают в уже привязанный чат с reverse-claim,
/// <see cref="MyChatMemberHandler"/> постит claim-объявление и помечает binding. Binding'и
/// грузятся через <see cref="ChatBindingRepository.GetManyByAsync"/> (untracked), поэтому
/// без явного <c>Update</c> SaveChanges не персистил <c>announcement_message_id</c> — объявление
/// улетало в Telegram, а UI вечно показывал «не опубликовано».
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class MyChatMemberHandlerTests : TelegramBotTestsBase
{
    private readonly ITelegramBotClient _bot = Substitute.For<ITelegramBotClient>();
    private readonly IAccessServiceClient _access = Substitute.For<IAccessServiceClient>();

    public MyChatMemberHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _bot.SendRequest(Arg.Any<IRequest<User>>(), Arg.Any<CancellationToken>())
            .Returns(new User { Id = 999, IsBot = true, FirstName = "Bot", Username = "testbot" });
        _bot.SendRequest(Arg.Any<IRequest<Message>>(), Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 555 });
        _access.GetPlanTelegramInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(ci.Arg<Guid>(), "Test Plan", "FULL_ALL", WelcomeMessage: null)));
    }

    [Fact]
    public async Task BotJoinsBoundChatWithReverseClaim_PersistsAnnouncementMessageId()
    {
        Guid planId = Guid.NewGuid();
        long chatId = -3001;
        await SeedClaimableBindingAsync(planId, chatId);

        await using TelegramBotDbContext db = BuildDbContext();
        MyChatMemberHandler handler = BuildHandler(db);

        await handler.HandleAsync(BuildBotJoinContext(chatId));

        // Объявление реально запостилось в Telegram.
        await _bot.Received().SendRequest(Arg.Any<IRequest<Message>>(), Arg.Any<CancellationToken>());

        // И message_id ПЕРСИСТНУЛСЯ — это суть регрессии #434.
        ChatBinding reloaded = await ExecuteInDb(db2 =>
            db2.ChatBindings.AsNoTracking().FirstAsync(b => b.TelegramChatId == chatId));
        Assert.Equal(555, reloaded.AnnouncementMessageId);
    }

    private MyChatMemberHandler BuildHandler(TelegramBotDbContext db)
    {
        var bindings = new ChatBindingRepository(db);
        var health = new ChatBindingHealthService(
            bindings, ChatApi, BuildTransactionManager(db),
            NullLogger<ChatBindingHealthService>.Instance);
        var announcer = new ClaimAnnouncementService(
            _bot, _access, NullLogger<ClaimAnnouncementService>.Instance);

        return new MyChatMemberHandler(
            BotNotifier,
            health,
            bindings,
            announcer,
            BuildTransactionManager(db),
            NullLogger<MyChatMemberHandler>.Instance);
    }

    private async Task SeedClaimableBindingAsync(Guid planId, long telegramChatId) =>
        await ExecuteInDb(async db =>
        {
            ChatBinding b = ChatBinding.Create(
                Guid.NewGuid(), planId, telegramChatId, ChatType.SUPERGROUP,
                "Plan chat", "https://t.me/+abc",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: true,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });

    private static UpdateContext BuildBotJoinContext(long chatId)
    {
        User bot = new() { Id = 999, IsBot = true, FirstName = "Bot", Username = "testbot" };
        ChatMemberUpdated memberUpdate = new()
        {
            Chat = new Chat { Id = chatId, Type = Telegram.Bot.Types.Enums.ChatType.Supergroup, Title = "Plan chat" },
            From = new User { Id = 111, FirstName = "Admin" },
            Date = DateTime.UtcNow,
            OldChatMember = new ChatMemberLeft { User = bot },
            NewChatMember = new ChatMemberAdministrator { User = bot },
        };

        Update update = new() { MyChatMember = memberUpdate };
        return new UpdateContext(update, Substitute.For<IServiceProvider>(), CancellationToken.None);
    }
}
