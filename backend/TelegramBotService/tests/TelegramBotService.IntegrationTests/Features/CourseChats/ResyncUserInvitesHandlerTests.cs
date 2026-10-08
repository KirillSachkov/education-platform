using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlatformAuth.Middleware;
using SharedKernel;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Features.CourseChats.UseCases;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// <c>POST /telegram/admin/users/{userId}/resync-invites/</c> (#444): admin/support-вариант
/// resync'а инвайтов для произвольного userId. Делегирует в тот же
/// <see cref="TelegramInviteResyncService"/>, что и user-facing endpoint.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class ResyncUserInvitesHandlerTests : TelegramBotTestsBase
{
    private readonly IAccessServiceClient _accessClient;
    private readonly UserScopedData _admin;

    public ResyncUserInvitesHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _accessClient = Substitute.For<IAccessServiceClient>();
        ConfigureCommunityPlanInfo(_accessClient);
        _admin = new UserScopedData();
        _admin.Authenticate(Guid.NewGuid(), "admin", "a@x.io", ["platform-moderator"]);
    }

    private static void ConfigureCommunityPlanInfo(IAccessServiceClient client) =>
        client.GetPlanTelegramInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<AccessService.Contracts.Plans.Dtos.PlanTelegramInfoDto, Error>(
                new AccessService.Contracts.Plans.Dtos.PlanTelegramInfoDto(
                    call.Arg<Guid>(), "Plan", "COURSE", null, call.Arg<Guid>(),
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

    [Fact]
    public async Task Handle_NoTelegramLink_ReturnsZeroAndTelegramLinkedFalse()
    {
        await using TelegramBotDbContext db = BuildDbContext();
        ResyncUserInvitesHandler handler = BuildHandler(db);

        var result = await handler.Handle(new ResyncUserInvitesCommand(Guid.NewGuid()), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.InvitesSent);
        Assert.False(result.Value.TelegramLinked);
    }

    [Fact]
    public async Task Handle_LinkedUserWithGrantAndBinding_SendsInviteDm()
    {
        Guid targetUserId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 820_010;
        long chatId = -8201;

        await ExecuteInDb(async db =>
        {
            await db.UserLinks.AddAsync(UserLink.Create(tgUserId, targetUserId, "u").Value);
            ChatBinding b = ChatBinding.Create(
                Guid.NewGuid(), planId, chatId, ChatType.SUPERGROUP,
                "Admin resync chat", "https://t.me/+adminresync",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });

        _accessClient.GetUserGrantsAsync(targetUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([ActiveGrant(planId, targetUserId)]));

        await using TelegramBotDbContext db = BuildDbContext();
        ResyncUserInvitesHandler handler = BuildHandler(db);

        var result = await handler.Handle(new ResyncUserInvitesCommand(targetUserId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.InvitesSent);
        Assert.True(result.Value.TelegramLinked);

        await BotNotifier.Received(1).SendTextAsync(
            tgUserId,
            Arg.Is<string>(s => s.Contains("Admin resync chat", StringComparison.Ordinal)),
            Arg.Is<InlineKeyboardMarkup?>(kb => kb != null
                && kb.InlineKeyboard.Single().Single().Url == "https://t.me/+adminresync"),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    private ResyncUserInvitesHandler BuildHandler(TelegramBotDbContext db)
    {
        TelegramInviteResyncService resync = new(
            new ChatBindingRepository(db),
            _accessClient,
            BotNotifier,
            BuildPlanWelcomeService(_accessClient),
            BuildMembershipChecker(),
            NullLogger<TelegramInviteResyncService>.Instance);

        return new ResyncUserInvitesHandler(
            BuildUserLinkRepository(db),
            resync,
            _admin,
            NullLogger<ResyncUserInvitesHandler>.Instance);
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
