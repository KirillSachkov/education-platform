using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using SharedKernel;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Messaging.Consumers;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.Consumers;

/// <summary>
/// На <c>user.telegram_linked</c> сервис пере-разрядает invite-DM по всем активным
/// plan-grants юзера. Логика общая с user-facing <c>/telegram/me/resync-invites</c>
/// через <see cref="TelegramInviteResyncService"/>.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class UserTelegramLinkedHandlerTests : TelegramBotTestsBase
{
    private readonly IAccessServiceClient _accessClient;

    public UserTelegramLinkedHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _accessClient = Substitute.For<IAccessServiceClient>();
        ConfigureCommunityPlanInfo(_accessClient);
    }

    private static void ConfigureCommunityPlanInfo(IAccessServiceClient client) =>
        client.GetPlanTelegramInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<AccessService.Contracts.Plans.Dtos.PlanTelegramInfoDto, Error>(
                new AccessService.Contracts.Plans.Dtos.PlanTelegramInfoDto(
                    call.Arg<Guid>(), "Plan", "COURSE", null, call.Arg<Guid>(),
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

    [Fact]
    public async Task Handle_HappyPath_SendsInviteDmForBoundChat()
    {
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 900_001;
        long chatId = -1001;

        await SeedChatBindingAsync(planId, chatId, "Plan chat", "https://t.me/+resync1");

        _accessClient.GetUserGrantsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([ActiveGrant(planId, userId)]));

        await using TelegramBotDbContext db = BuildDbContext();
        UserTelegramLinkedHandler handler = BuildHandler(db);

        await handler.Handle(new UserTelegramLinked(userId, tgUserId, "alice"), default);

        await BotNotifier.Received(1).SendTextAsync(
            tgUserId,
            Arg.Is<string>(s => s.Contains("Plan chat", StringComparison.Ordinal)),
            Arg.Is<InlineKeyboardMarkup?>(kb => kb != null
                && kb.InlineKeyboard.Single().Single().Url == "https://t.me/+resync1"),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoActiveGrants_NoOp()
    {
        Guid userId = Guid.NewGuid();
        long tgUserId = 900_002;

        // Bindings присутствуют, но grants нет — handler выходит после AccessService check.
        await SeedChatBindingAsync(Guid.NewGuid(), -1002, "Other plan", "https://t.me/+x");

        _accessClient.GetUserGrantsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([]));

        await using TelegramBotDbContext db = BuildDbContext();
        UserTelegramLinkedHandler handler = BuildHandler(db);

        await handler.Handle(new UserTelegramLinked(userId, tgUserId, null), default);

        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AccessServiceFailure_ThrowsForWolverineRetry()
    {
        Guid userId = Guid.NewGuid();
        long tgUserId = 900_003;

        _accessClient.GetUserGrantsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<PlanGrantDto>, Error>(
                Error.Failure("access.service.unavailable", "down")));

        await using TelegramBotDbContext db = BuildDbContext();
        UserTelegramLinkedHandler handler = BuildHandler(db);

        await Assert.ThrowsAsync<SharedKernel.Exceptions.TransientException>(() =>
            handler.Handle(new UserTelegramLinked(userId, tgUserId, null), default));

        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(), Arg.Any<CancellationToken>());
    }

    private UserTelegramLinkedHandler BuildHandler(TelegramBotDbContext db)
    {
        TelegramInviteResyncService resync = new(
            new ChatBindingRepository(db),
            _accessClient,
            BotNotifier,
            BuildPlanWelcomeService(_accessClient),
            BuildMembershipChecker(),
            NullLogger<TelegramInviteResyncService>.Instance);

        return new UserTelegramLinkedHandler(
            resync,
            NullLogger<UserTelegramLinkedHandler>.Instance);
    }

    private async Task SeedChatBindingAsync(
        Guid planId, long telegramChatId, string chatTitle, string inviteLink) =>
        await ExecuteInDb(async db =>
        {
            ChatBinding b = ChatBinding.Create(
                Guid.NewGuid(), planId, telegramChatId, ChatType.SUPERGROUP,
                chatTitle, inviteLink,
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });

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
