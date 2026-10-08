using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Access.Events;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Messaging.Consumers;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.Consumers;

/// <summary>
/// F6: PlanGrantRevoked/Expired → kick из bound chats с AutoKickOnRevoke=true.
/// Покрывает: happy path, AutoKickOnRevoke=false (skip), no UserLink (nothing to kick),
/// confirmed NotMember (skip kick).
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class PlanGrantRevokedTelegramHandlerTests : TelegramBotTestsBase
{
    private readonly IBotDecisionLogger _audit;
    private readonly IAccessServiceClient _accessClient;

    public PlanGrantRevokedTelegramHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _audit = Substitute.For<IBotDecisionLogger>();
        _accessClient = Substitute.For<IAccessServiceClient>();
        _accessClient.GetUserGrantsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, SharedKernel.Error>([]));
        _accessClient.GetPlanTelegramInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<PlanTelegramInfoDto, SharedKernel.Error>(
                new PlanTelegramInfoDto(
                    call.Arg<Guid>(), "Plan", "COURSE", null, call.Arg<Guid>(),
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));
    }

    [Fact]
    public async Task Handle_HappyPath_CallsKickChatMember()
    {
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 800_001;
        long chatId = -1001;

        await SeedUserLinkAsync(tgUserId, userId);
        await SeedChatBindingAsync(planId, chatId, autoKickOnRevoke: true);

        // Default ChatApi.GetChatMemberAsync → failure → Unknown → fail-closed → kick.
        ChatApi.KickChatMemberAsync(chatId, tgUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Success(true));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantRevokedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildRevokedEvent(userId, planId), default);

        await ChatApi.Received(1).KickChatMemberAsync(chatId, tgUserId, Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(chatId, tgUserId,
            Domain.Audit.BotDecisions.AUTO_KICK_ENROLLMENT_REVOKED,
            reason: Arg.Any<string?>(),
            planId: planId,
            ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AutoKickFlagDisabled_DoesNotKick()
    {
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 800_002;
        long chatId = -1002;

        await SeedUserLinkAsync(tgUserId, userId);
        await SeedChatBindingAsync(planId, chatId, autoKickOnRevoke: false);

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantRevokedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildRevokedEvent(userId, planId), default);

        await ChatApi.DidNotReceiveWithAnyArgs().KickChatMemberAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoUserLink_DoesNothing()
    {
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        await SeedChatBindingAsync(planId, telegramChatId: -1003, autoKickOnRevoke: true);

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantRevokedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildRevokedEvent(userId, planId), default);

        await ChatApi.DidNotReceiveWithAnyArgs().KickChatMemberAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConfirmedNotMember_SkipsKick()
    {
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 800_004;
        long chatId = -1004;

        await SeedUserLinkAsync(tgUserId, userId);
        await SeedChatBindingAsync(planId, chatId, autoKickOnRevoke: true);

        // ChatApi confirmed: юзер не в чате — skip kick.
        ChatApi.GetChatMemberAsync(chatId, tgUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Success(
                new ChatMemberInfo(tgUserId, ChatMembership.NOT_MEMBER)));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantRevokedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildRevokedEvent(userId, planId), default);

        await ChatApi.DidNotReceiveWithAnyArgs().KickChatMemberAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PlanGrantExpiredEvent_AlsoTriggersKick()
    {
        // F6 слушает оба event'а — revoked и expired.
        Guid userId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        long tgUserId = 800_005;
        long chatId = -1005;

        await SeedUserLinkAsync(tgUserId, userId);
        await SeedChatBindingAsync(planId, chatId, autoKickOnRevoke: true);
        ChatApi.KickChatMemberAsync(chatId, tgUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Success(true));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantRevokedTelegramHandler handler = BuildHandler(db);

        PlanGrantExpired evt = new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: planId,
            PlanTier: "COURSE",
            PlanAuthorId: Guid.NewGuid(),
            CourseId: null,
            ExpiredAt: DateTimeOffset.UtcNow);

        await handler.Handle(evt, default);

        await ChatApi.Received(1).KickChatMemberAsync(chatId, tgUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_KickRateLimited_ThrowsForWolverineRetry()
    {
        Guid userId = Guid.CreateVersion7();
        Guid planId = Guid.CreateVersion7();
        long tgUserId = 800_006;
        long chatId = -1006;

        await SeedUserLinkAsync(tgUserId, userId);
        await SeedChatBindingAsync(planId, chatId, autoKickOnRevoke: true);
        ChatApi.KickChatMemberAsync(chatId, tgUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Failure(ChatApiErrorCode.RateLimited, "retry after 30"));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantRevokedTelegramHandler handler = BuildHandler(db);

        await Assert.ThrowsAsync<SharedKernel.Exceptions.TransientException>(() =>
            handler.Handle(BuildRevokedEvent(userId, planId), default));
    }

    [Fact]
    public async Task Handle_KickServiceUnavailable_ThrowsForWolverineRetry()
    {
        Guid userId = Guid.CreateVersion7();
        Guid planId = Guid.CreateVersion7();
        long telegramUserId = 800_011;
        long chatId = -1011;

        await SeedUserLinkAsync(telegramUserId, userId);
        await SeedChatBindingAsync(planId, chatId, autoKickOnRevoke: true);
        ChatApi.KickChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Failure(
                ChatApiErrorCode.ServiceUnavailable,
                "Telegram 500"));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantRevokedTelegramHandler handler = BuildHandler(db);

        await Assert.ThrowsAsync<SharedKernel.Exceptions.TransientException>(() =>
            handler.Handle(BuildRevokedEvent(userId, planId), default));
    }

    [Fact]
    public async Task Handle_UserRetainsAccessThroughAnotherPlan_DoesNotKick()
    {
        Guid userId = Guid.CreateVersion7();
        Guid revokedPlanId = Guid.CreateVersion7();
        Guid remainingPlanId = Guid.CreateVersion7();
        long telegramUserId = 800_007;
        long chatId = -1007;

        await SeedUserLinkAsync(telegramUserId, userId);
        await SeedChatBindingAsync(revokedPlanId, chatId, autoKickOnRevoke: true);
        await SeedChatBindingAsync(remainingPlanId, chatId, autoKickOnRevoke: true);
        _accessClient.GetUserGrantsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, SharedKernel.Error>([
                ActiveGrant(userId, remainingPlanId)
            ]));
        _accessClient.GetPlanTelegramInfoAsync(remainingPlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<AccessService.Contracts.Plans.Dtos.PlanTelegramInfoDto, SharedKernel.Error>(
                new AccessService.Contracts.Plans.Dtos.PlanTelegramInfoDto(
                    remainingPlanId,
                    "Remaining",
                    "COURSE",
                    null,
                    remainingPlanId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantRevokedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildRevokedEvent(userId, revokedPlanId), default);

        await ChatApi.DidNotReceiveWithAnyArgs().KickChatMemberAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserHasAnotherActiveGrantForSamePlan_DoesNotKick()
    {
        Guid userId = Guid.CreateVersion7();
        Guid planId = Guid.CreateVersion7();
        long telegramUserId = 800_010;
        long chatId = -1010;

        await SeedUserLinkAsync(telegramUserId, userId);
        await SeedChatBindingAsync(planId, chatId, autoKickOnRevoke: true);
        _accessClient.GetUserGrantsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PlanGrantDto>, SharedKernel.Error>([
                ActiveGrant(userId, planId)
            ]));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantRevokedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildRevokedEvent(userId, planId), default);

        await ChatApi.DidNotReceiveWithAnyArgs().KickChatMemberAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExpiredTrial_KicksFromCanonicalPlanChat()
    {
        Guid userId = Guid.CreateVersion7();
        Guid trialPlanId = Guid.CreateVersion7();
        Guid canonicalPlanId = Guid.CreateVersion7();
        long telegramUserId = 800_008;
        long chatId = -1008;

        await SeedUserLinkAsync(telegramUserId, userId);
        await SeedChatBindingAsync(canonicalPlanId, chatId, autoKickOnRevoke: true);
        _accessClient.GetPlanTelegramInfoAsync(trialPlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, SharedKernel.Error>(
                new PlanTelegramInfoDto(
                    trialPlanId, "Trial", "FULL_ALL", null, canonicalPlanId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));
        ChatApi.KickChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Success(true));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantRevokedTelegramHandler handler = BuildHandler(db);
        var evt = new PlanGrantExpired(
            Guid.CreateVersion7(), userId, trialPlanId, "FULL_ALL", Guid.CreateVersion7(), null,
            DateTimeOffset.UtcNow, null, canonicalPlanId);

        await handler.Handle(evt, default);

        await ChatApi.Received(1).KickChatMemberAsync(
            chatId, telegramUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DelayedAfterPlanHardDelete_FallsBackToDirectBinding()
    {
        Guid userId = Guid.CreateVersion7();
        Guid planId = Guid.CreateVersion7();
        long telegramUserId = 800_009;
        long chatId = -1009;

        await SeedUserLinkAsync(telegramUserId, userId);
        await SeedChatBindingAsync(planId, chatId, autoKickOnRevoke: true);
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PlanTelegramInfoDto, SharedKernel.Error>(
                SharedKernel.Error.NotFound("plan.not_found", "deleted")));
        ChatApi.KickChatMemberAsync(chatId, telegramUserId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Success(true));

        await using TelegramBotDbContext db = BuildDbContext();
        PlanGrantRevokedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(BuildRevokedEvent(userId, planId), default);

        await ChatApi.Received(1).KickChatMemberAsync(
            chatId, telegramUserId, Arg.Any<CancellationToken>());
    }

    private PlanGrantRevokedTelegramHandler BuildHandler(TelegramBotDbContext db) =>
        new(
            BuildUserLinkRepository(db),
            new ChatBindingRepository(db),
            _accessClient,
            ChatApi,
            BuildMembershipChecker(),
            _audit,
            NullLogger<PlanGrantRevokedTelegramHandler>.Instance);

    private async Task SeedUserLinkAsync(long telegramUserId, Guid platformUserId) =>
        await ExecuteInDb(async db =>
        {
            UserLink link = UserLink.Create(telegramUserId, platformUserId, "u").Value;
            await db.UserLinks.AddAsync(link);
            await db.SaveChangesAsync();
        });

    private async Task SeedChatBindingAsync(Guid planId, long telegramChatId, bool autoKickOnRevoke) =>
        await ExecuteInDb(async db =>
        {
            ChatBinding b = ChatBinding.Create(
                Guid.NewGuid(), planId, telegramChatId, ChatType.SUPERGROUP,
                chatTitle: "Plan chat", inviteLink: "https://t.me/+abc",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: autoKickOnRevoke,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });

    private static PlanGrantRevoked BuildRevokedEvent(Guid userId, Guid planId) =>
        new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: planId,
            Reason: "admin revoke",
            RevokedAt: DateTimeOffset.UtcNow);

    private static PlanGrantDto ActiveGrant(Guid userId, Guid planId) =>
        new(
            Guid.CreateVersion7(), userId, planId, "PURCHASE", null,
            DateTimeOffset.UtcNow, null, "ACTIVE", null, null);
}
