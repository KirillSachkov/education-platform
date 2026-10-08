using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot;
using TelegramBotService.Contracts.Requests;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Features.CourseChats.UseCases;
using TelegramBotService.Domain;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// UpdateChatBindingFlagsHandler: 404 для несуществующего binding, флаги обновляются,
/// channel-чаты клампят MembershipGrantsEnrollment + EnforceMembership в false.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class UpdateChatBindingFlagsHandlerTests : TelegramBotTestsBase
{
    private readonly UpdateChatBindingFlagsValidator _validator = new();
    private readonly IAccessServiceClient _accessClient;

    public UpdateChatBindingFlagsHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _accessClient = Substitute.For<IAccessServiceClient>();
        _accessClient.GetPlanTelegramInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    call.Arg<Guid>(), "Plan", "COURSE", null, call.Arg<Guid>(),
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));
    }

    [Fact]
    public async Task Handle_BindingNotFound_ReturnsError()
    {
        await using TelegramBotDbContext db = BuildDbContext();
        UpdateChatBindingFlagsHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new UpdateChatBindingFlagsCommand(
                Guid.NewGuid(),
                new UpdateChatBindingFlagsRequest(false, false, false, false)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(TelegramBotErrors.ChatBindingNotFound().Messages[0].Code,
            result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task Handle_SupergroupChat_UpdatesAllFlags()
    {
        Guid bindingId = Guid.NewGuid();
        await SeedBindingAsync(bindingId, ChatType.SUPERGROUP);

        await using TelegramBotDbContext db = BuildDbContext();
        UpdateChatBindingFlagsHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new UpdateChatBindingFlagsCommand(
                bindingId,
                new UpdateChatBindingFlagsRequest(
                    EnrollmentGrantsMembership: false,
                    MembershipGrantsEnrollment: true,
                    AutoKickOnRevoke: true,
                    EnforceMembership: true)),
            default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.EnrollmentGrantsMembership);
        Assert.True(result.Value.MembershipGrantsEnrollment);
        Assert.True(result.Value.AutoKickOnRevoke);
        Assert.True(result.Value.EnforceMembership);
    }

    [Fact]
    public async Task Handle_ChannelChat_AllowsMembershipButClampsEnforceMembershipToFalse()
    {
        // Channel reverse-claim теперь разрешён (#416) — MembershipGrantsEnrollment сохраняется,
        // EnforceMembership остаётся group-only → clamped.
        Guid bindingId = Guid.NewGuid();
        await SeedBindingAsync(bindingId, ChatType.CHANNEL);

        await using TelegramBotDbContext db = BuildDbContext();
        UpdateChatBindingFlagsHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new UpdateChatBindingFlagsCommand(
                bindingId,
                new UpdateChatBindingFlagsRequest(
                    EnrollmentGrantsMembership: true,
                    MembershipGrantsEnrollment: true,    // client requested true
                    AutoKickOnRevoke: false,
                    EnforceMembership: true)),           // client requested true
            default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.MembershipGrantsEnrollment);   // preserved for channel (#416)
        Assert.False(result.Value.EnforceMembership);           // clamped — group-only
        Assert.True(result.Value.EnrollmentGrantsMembership);
    }

    [Fact]
    public async Task Handle_EnableEnrollmentWithoutCommunityCapability_IsRejected()
    {
        Guid bindingId = Guid.CreateVersion7();
        await SeedBindingAsync(bindingId, ChatType.SUPERGROUP);
        Guid planId = await ExecuteInDb(db => db.ChatBindings
            .Where(binding => binding.Id == bindingId)
            .Select(binding => binding.PlanId)
            .SingleAsync());
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    planId, "Learn only", "COURSE", null, planId, ["VIEW_MATERIALS"])));

        await using TelegramBotDbContext db = BuildDbContext();
        UpdateChatBindingFlagsHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new UpdateChatBindingFlagsCommand(
                bindingId,
                new UpdateChatBindingFlagsRequest(true, false, false, false)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("telegram.chat.plan.community_access_required", result.Error.Messages[0].Code);
    }

    private UpdateChatBindingFlagsHandler BuildHandler(TelegramBotDbContext db) =>
        new(
            new ChatBindingRepository(db),
            _accessClient,
            BuildTransactionManager(db),
            new ClaimAnnouncementService(
                Substitute.For<ITelegramBotClient>(),
                Substitute.For<IAccessServiceClient>(),
                NullLogger<ClaimAnnouncementService>.Instance),
            _validator);

    private async Task SeedBindingAsync(Guid bindingId, ChatType chatType) =>
        await ExecuteInDb(async db =>
        {
            ChatBinding b = ChatBinding.Create(
                bindingId, Guid.NewGuid(), -7777, chatType,
                "X", "https://t.me/+abc",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });
}
