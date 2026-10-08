using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using NSubstitute;
using PlatformAuth.Middleware;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Features.Onboarding;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.Onboarding;

[Collection(nameof(TelegramBotTestCollection))]
public sealed class GetMyPlanOnboardingStatusHandlerTests : TelegramBotTestsBase
{
    public GetMyPlanOnboardingStatusHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Handle_WithoutActiveGrant_DoesNotExposePlanInviteLinks()
    {
        Guid userId = Guid.CreateVersion7();
        Guid planId = Guid.CreateVersion7();
        await ExecuteInDb(async db =>
        {
            ChatBinding binding = ChatBinding.Create(
                Guid.CreateVersion7(), planId, -100_773, ChatType.SUPERGROUP,
                "Private plan chat", "https://t.me/+private-secret",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.CreateVersion7()).Value;
            await db.ChatBindings.AddAsync(binding);
            await db.SaveChangesAsync();
        });

        IAccessServiceClient accessClient = Substitute.For<IAccessServiceClient>();
        accessClient.GetUserGrantsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([]));
        var user = new UserScopedData();
        user.Authenticate(userId, "participant", "participant@example.com", ["platform-participant"]);

        await using TelegramBotDbContext db = BuildDbContext();
        var handler = new GetMyPlanOnboardingStatusHandler(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            accessClient,
            user);

        Result<PlanOnboardingStatusResponse, Error> result = await handler.Handle(
            new GetMyPlanOnboardingStatusQuery(planId),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.AUTHORIZATION, result.Error.Type);
        Assert.Equal("telegram.plan.access_denied", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task Handle_TrialGrant_ReturnsOnlyCanonicalPlanChats()
    {
        Guid userId = Guid.CreateVersion7();
        Guid trialPlanId = Guid.CreateVersion7();
        Guid canonicalPlanId = Guid.CreateVersion7();
        Guid unrelatedPlanId = Guid.CreateVersion7();
        await ExecuteInDb(async db =>
        {
            await db.ChatBindings.AddRangeAsync(
                CreateBinding(canonicalPlanId, -100_774, "https://t.me/+canonical"),
                CreateBinding(unrelatedPlanId, -100_775, "https://t.me/+unrelated"));
            await db.SaveChangesAsync();
        });

        IAccessServiceClient accessClient = Substitute.For<IAccessServiceClient>();
        accessClient.GetUserGrantsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, Error>([
                new PlanGrantDto(
                    Guid.CreateVersion7(), userId, trialPlanId, "PURCHASE", null,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), "ACTIVE", null, null)
            ]));
        accessClient.GetPlanTelegramInfoAsync(trialPlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    trialPlanId, "Trial", "FULL_ALL", null, canonicalPlanId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));
        var user = new UserScopedData();
        user.Authenticate(userId, "participant", "participant@example.com", ["platform-participant"]);

        await using TelegramBotDbContext db = BuildDbContext();
        var handler = new GetMyPlanOnboardingStatusHandler(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            accessClient,
            user);

        Result<PlanOnboardingStatusResponse, Error> result = await handler.Handle(
            new GetMyPlanOnboardingStatusQuery(trialPlanId),
            default);

        Assert.True(result.IsSuccess);
        PlanChatDto chat = Assert.Single(result.Value.Chats);
        Assert.Equal("https://t.me/+canonical", chat.JoinUrl);
    }

    private static ChatBinding CreateBinding(Guid planId, long chatId, string inviteLink) =>
        ChatBinding.Create(
            Guid.CreateVersion7(), planId, chatId, ChatType.SUPERGROUP,
            "Private plan chat", inviteLink,
            enrollmentGrantsMembership: true,
            membershipGrantsEnrollment: false,
            autoKickOnRevoke: false,
            enforceMembership: false,
            createdBy: Guid.CreateVersion7()).Value;
}
