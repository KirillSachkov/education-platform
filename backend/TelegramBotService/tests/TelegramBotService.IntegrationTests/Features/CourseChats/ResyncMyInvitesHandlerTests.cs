using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
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
/// <c>POST /telegram/me/resync-invites</c>: пере-разсылает invite-DM для всех активных
/// grant'ов + bound chat'ов. Возвращает <see cref="ResyncInvitesResponse"/> с
/// <c>InvitesSent</c> и <c>TelegramLinked</c>.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class ResyncMyInvitesHandlerTests : TelegramBotTestsBase
{
    private readonly IAccessServiceClient _accessClient;
    private readonly UserScopedData _user;

    public ResyncMyInvitesHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _accessClient = Substitute.For<IAccessServiceClient>();
        ConfigureCommunityPlanInfo(_accessClient);
        _user = new UserScopedData();
        _user.Authenticate(Guid.NewGuid(), "u", "u@x.io", ["platform-participant"]);
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
        ResyncMyInvitesHandler handler = BuildHandler(db);

        var result = await handler.Handle(new ResyncMyInvitesCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.InvitesSent);
        Assert.False(result.Value.TelegramLinked);
    }

    [Fact]
    public async Task Handle_LinkedUserWithGrantAndBinding_SendsInviteDm()
    {
        Guid planId = Guid.NewGuid();
        long tgUserId = 700_010;
        long chatId = -9101;

        await ExecuteInDb(async db =>
        {
            await db.UserLinks.AddAsync(UserLink.Create(tgUserId, _user.UserId, "u").Value);
            ChatBinding b = ChatBinding.Create(
                Guid.NewGuid(), planId, chatId, ChatType.SUPERGROUP,
                "Resync chat", "https://t.me/+resync",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });

        _accessClient.GetUserGrantsAsync(_user.UserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([ActiveGrant(planId, _user.UserId)]));

        await using TelegramBotDbContext db = BuildDbContext();
        ResyncMyInvitesHandler handler = BuildHandler(db);

        var result = await handler.Handle(new ResyncMyInvitesCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.InvitesSent);
        Assert.True(result.Value.TelegramLinked);

        await BotNotifier.Received(1).SendTextAsync(
            tgUserId,
            Arg.Is<string>(s => s.Contains("Resync chat", StringComparison.Ordinal)),
            Arg.Is<InlineKeyboardMarkup?>(kb => kb != null
                && kb.InlineKeyboard.Single().Single().Url == "https://t.me/+resync"),
            Arg.Any<Telegram.Bot.Types.Enums.ParseMode>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SoftBlockedLink_SkipsResync()
    {
        Guid planId = Guid.NewGuid();
        long tgUserId = 700_011;

        await ExecuteInDb(async db =>
        {
            UserLink link = UserLink.Create(tgUserId, _user.UserId, "u").Value;
            link.Block("bot_blocked");
            await db.UserLinks.AddAsync(link);
            await db.SaveChangesAsync();
        });

        await using TelegramBotDbContext db = BuildDbContext();
        ResyncMyInvitesHandler handler = BuildHandler(db);

        var result = await handler.Handle(new ResyncMyInvitesCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.InvitesSent);
        Assert.True(result.Value.TelegramLinked);  // link есть, просто soft-blocked

        // Сам resync даже не вызывается.
        await _accessClient.DidNotReceiveWithAnyArgs().GetUserGrantsAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private ResyncMyInvitesHandler BuildHandler(TelegramBotDbContext db)
    {
        TelegramInviteResyncService resync = new(
            new ChatBindingRepository(db),
            _accessClient,
            BotNotifier,
            BuildPlanWelcomeService(_accessClient),
            BuildMembershipChecker(),
            NullLogger<TelegramInviteResyncService>.Instance);

        return new ResyncMyInvitesHandler(
            BuildUserLinkRepository(db),
            resync,
            _user,
            NullLogger<ResyncMyInvitesHandler>.Instance);
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
