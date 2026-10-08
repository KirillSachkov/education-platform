using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBotFlow.Core.Screens;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.MainMenu.Screens;
using TelegramBotService.Core.Options;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;
using UpdateContext = TelegramBotFlow.Core.Context.UpdateContext;

namespace TelegramBotService.IntegrationTests.Features.MainMenu;

/// <summary>
///     Regression: после миграции <c>RebindChatsAndDecisionsToPlans</c> (#65) экран
///     должен резолвить статус юзера через AccessService PlanGrant'ы, а не через
///     ProgressService course enrollments. Issue #206.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class MainMenuScreenTests : TelegramBotTestsBase
{
    public MainMenuScreenTests(TelegramBotTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task RenderAsync_LinkedUserWithActiveGrant_ShowsPlanCount()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 555_300_001;
        Guid planId = Guid.NewGuid();
        long chatId = -1001_222_333_444;

        await ExecuteInDb(async db =>
        {
            await db.UserLinks.AddAsync(UserLink.Create(telegramUserId, platformUserId, "alice").Value);
            await db.ChatBindings.AddAsync(ChatBinding.Create(
                Guid.NewGuid(), planId, chatId, TelegramBotService.Domain.CourseChats.ChatType.SUPERGROUP,
                "Course Chat", "https://t.me/+abc",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value);
            await db.SaveChangesAsync();
        });

        IAccessServiceClient accessClient = Substitute.For<IAccessServiceClient>();
        accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([
                ActiveGrant(planId, platformUserId)
            ]));
        accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<AccessService.Contracts.Plans.Dtos.PlanTelegramInfoDto, Error>(
                new AccessService.Contracts.Plans.Dtos.PlanTelegramInfoDto(
                    planId, "Plan", "COURSE", null, planId,
                    [TelegramBotService.Core.Features.CourseChats.Services.TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

        await using TelegramBotDbContext db = BuildDbContext();
        MainMenuScreen screen = new(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            accessClient,
            Options.Create(new TelegramNotificationOptions { FrontendBaseUrl = "https://example.com" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MainMenuScreen>.Instance);

        ScreenView view = await screen.RenderAsync(BuildContextFor(telegramUserId));

        Assert.DoesNotContain("Пока нет планов", view.Text, StringComparison.Ordinal);
        Assert.Contains("<b>Планов:</b> 1", view.Text, StringComparison.Ordinal);
        Assert.Contains("<b>чатов доступно:</b> 1", view.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_LinkedUserWithGrantForOtherPlan_DoesNotCountForeignBinding()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 555_300_004;
        Guid grantedPlanId = Guid.NewGuid();
        Guid otherPlanId = Guid.NewGuid();

        await ExecuteInDb(async db =>
        {
            await db.UserLinks.AddAsync(UserLink.Create(telegramUserId, platformUserId, "carol").Value);
            // Binding для плана, на который у юзера НЕТ grant'а — должен быть исключён SQL-предикатом.
            await db.ChatBindings.AddAsync(ChatBinding.Create(
                Guid.NewGuid(), otherPlanId, -1001_000_000_001L, TelegramBotService.Domain.CourseChats.ChatType.SUPERGROUP,
                "Other Chat", "https://t.me/+other",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value);
            await db.SaveChangesAsync();
        });

        IAccessServiceClient accessClient = Substitute.For<IAccessServiceClient>();
        accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([
                ActiveGrant(grantedPlanId, platformUserId)
            ]));

        await using TelegramBotDbContext db = BuildDbContext();
        MainMenuScreen screen = new(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            accessClient,
            Options.Create(new TelegramNotificationOptions { FrontendBaseUrl = "https://example.com" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MainMenuScreen>.Instance);

        ScreenView view = await screen.RenderAsync(BuildContextFor(telegramUserId));

        Assert.Contains("<b>Планов:</b> 1", view.Text, StringComparison.Ordinal);
        Assert.Contains("<b>чатов доступно:</b> 0", view.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_LinkedUserWithoutActiveGrants_ShowsNoPlansMessage()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 555_300_002;

        await ExecuteInDb(async db =>
        {
            await db.UserLinks.AddAsync(UserLink.Create(telegramUserId, platformUserId, "bob").Value);
            await db.SaveChangesAsync();
        });

        IAccessServiceClient accessClient = Substitute.For<IAccessServiceClient>();
        accessClient.GetUserGrantsAsync(platformUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([]));

        await using TelegramBotDbContext db = BuildDbContext();
        MainMenuScreen screen = new(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            accessClient,
            Options.Create(new TelegramNotificationOptions { FrontendBaseUrl = "https://example.com" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MainMenuScreen>.Instance);

        ScreenView view = await screen.RenderAsync(BuildContextFor(telegramUserId));

        Assert.Contains("Пока нет планов", view.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_UnlinkedUser_ShowsConversionFunnel()
    {
        long telegramUserId = 555_300_003;

        IAccessServiceClient accessClient = Substitute.For<IAccessServiceClient>();

        await using TelegramBotDbContext db = BuildDbContext();
        MainMenuScreen screen = new(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            accessClient,
            Options.Create(new TelegramNotificationOptions { FrontendBaseUrl = "https://example.com" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MainMenuScreen>.Instance);

        ScreenView view = await screen.RenderAsync(BuildContextFor(telegramUserId));

        Assert.Contains("привяжи аккаунт платформы", view.Text, StringComparison.Ordinal);
        await accessClient.DidNotReceive().GetUserGrantsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static PlanGrantDto ActiveGrant(Guid planId, Guid userId) =>
        new(
            Id: Guid.NewGuid(),
            UserId: userId,
            PlanId: planId,
            Source: "INVITE_LINK",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Status: "ACTIVE",
            RevokedAt: null,
            RevokeReason: null,
            TelegramBindingPlanId: planId,
            Capabilities: [
                TelegramBotService.Core.Features.CourseChats.Services.TelegramGrantChatAccessResolver
                    .COMMUNITY_ACCESS
            ]);

    private static UpdateContext BuildContextFor(long telegramUserId)
    {
        Update update = new()
        {
            Message = new Message
            {
                Id = 1,
                Text = "/start",
                Date = DateTime.UtcNow,
                From = new User { Id = telegramUserId, FirstName = "T" },
                Chat = new Chat { Id = telegramUserId, Type = Telegram.Bot.Types.Enums.ChatType.Private }
            }
        };

        return new UpdateContext(update, Substitute.For<IServiceProvider>(), CancellationToken.None);
    }
}
