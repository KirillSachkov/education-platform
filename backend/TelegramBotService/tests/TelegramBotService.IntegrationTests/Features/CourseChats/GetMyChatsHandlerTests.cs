using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlatformAuth.Middleware;
using SharedKernel;
using TelegramBotService.Core.Features.CourseChats.UseCases;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// GetMyChatsHandler (<c>GET /telegram/me/chats</c>): возвращает только bindings,
/// у которых пользователь имеет активный plan-grant.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class GetMyChatsHandlerTests : TelegramBotTestsBase
{
    private readonly IAccessServiceClient _accessClient;
    private readonly UserScopedData _user;

    public GetMyChatsHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _accessClient = Substitute.For<IAccessServiceClient>();
        ConfigureCommunityPlanInfo(_accessClient);
        _user = new UserScopedData();
        _user.Authenticate(Guid.NewGuid(), "alice", "a@x.io", ["platform-participant"]);
    }

    private static void ConfigureCommunityPlanInfo(IAccessServiceClient client) =>
        client.GetPlanTelegramInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<AccessService.Contracts.Plans.Dtos.PlanTelegramInfoDto, Error>(
                new AccessService.Contracts.Plans.Dtos.PlanTelegramInfoDto(
                    call.Arg<Guid>(), "Plan", "COURSE", null, call.Arg<Guid>(),
                    [TelegramBotService.Core.Features.CourseChats.Services.TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

    [Fact]
    public async Task Handle_NoActiveGrants_ReturnsEmpty()
    {
        _accessClient.GetUserGrantsAsync(_user.UserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([]));

        await using TelegramBotDbContext db = BuildDbContext();
        GetMyChatsHandler handler = BuildHandler(db);

        var result = await handler.Handle(new GetMyChatsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_ActiveGrantWithBoundChat_ReturnsBinding()
    {
        Guid planId = Guid.NewGuid();
        Guid otherPlanId = Guid.NewGuid();
        long chatId = -9001;
        long otherChatId = -9002;
        long tgUserId = 700_009;

        await SeedUserLinkAsync(tgUserId, _user.UserId);
        await SeedBindingAsync(planId, chatId, "Accessible chat");
        await SeedBindingAsync(otherPlanId, otherChatId, "Foreign chat");

        _accessClient.GetUserGrantsAsync(_user.UserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([
                ActiveGrant(planId, _user.UserId)
            ]));

        await using TelegramBotDbContext db = BuildDbContext();
        GetMyChatsHandler handler = BuildHandler(db);

        var result = await handler.Handle(new GetMyChatsQuery(), default);

        Assert.True(result.IsSuccess);
        var only = Assert.Single(result.Value);
        Assert.Equal(chatId, only.TelegramChatId);
        Assert.Equal("Accessible chat", only.ChatTitle);
    }

    private GetMyChatsHandler BuildHandler(TelegramBotDbContext db) =>
        new(
            new ChatBindingRepository(db),
            BuildUserLinkRepository(db),
            _accessClient,
            BuildMembershipChecker(),
            _user,
            NullLogger<GetMyChatsHandler>.Instance);

    private async Task SeedUserLinkAsync(long telegramUserId, Guid platformUserId) =>
        await ExecuteInDb(async db =>
        {
            UserLink link = UserLink.Create(telegramUserId, platformUserId, "u").Value;
            await db.UserLinks.AddAsync(link);
            await db.SaveChangesAsync();
        });

    private async Task SeedBindingAsync(Guid planId, long chatId, string title) =>
        await ExecuteInDb(async db =>
        {
            ChatBinding b = ChatBinding.Create(
                Guid.NewGuid(), planId, chatId, ChatType.SUPERGROUP,
                title, "https://t.me/+abc",
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
