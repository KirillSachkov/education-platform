using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Access.Events;
using TelegramBotService.Core.Messaging.Consumers;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.Consumers;

/// <summary>
/// Issue #417: на <c>plan.hard_deleted</c> отвязываем (удаляем) все chat-binding'и удалённого
/// плана + best-effort revoke invite-link. Покрывает: happy path (только binding'и этого плана),
/// идемпотентность (нет binding'ов → no-op).
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class PlanHardDeletedTelegramHandlerTests : TelegramBotTestsBase
{
    public PlanHardDeletedTelegramHandlerTests(TelegramBotTestFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Handle_RemovesOnlyDeletedPlanBindings_AndRevokesInviteLinks()
    {
        Guid deletedPlanId = Guid.NewGuid();
        Guid otherPlanId = Guid.NewGuid();

        await SeedChatBindingAsync(deletedPlanId, telegramChatId: -2001);
        await SeedChatBindingAsync(deletedPlanId, telegramChatId: -2002);
        await SeedChatBindingAsync(otherPlanId, telegramChatId: -2003);

        await using TelegramBotDbContext db = BuildDbContext();
        PlanHardDeletedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(new PlanHardDeleted(deletedPlanId, AuthorId: Guid.NewGuid()), default);

        await ExecuteInDb(async assertDb =>
        {
            Assert.False(await assertDb.ChatBindings.AnyAsync(b => b.PlanId == deletedPlanId));
            Assert.True(await assertDb.ChatBindings.AnyAsync(b => b.PlanId == otherPlanId));
        });

        await ChatApi.Received(1).RevokeChatInviteLinkAsync(
            -2001, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await ChatApi.Received(1).RevokeChatInviteLinkAsync(
            -2002, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await ChatApi.DidNotReceive().RevokeChatInviteLinkAsync(
            -2003, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoBindings_IsNoOp()
    {
        await using TelegramBotDbContext db = BuildDbContext();
        PlanHardDeletedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(new PlanHardDeleted(Guid.NewGuid(), AuthorId: Guid.NewGuid()), default);

        await ChatApi.DidNotReceiveWithAnyArgs().RevokeChatInviteLinkAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private PlanHardDeletedTelegramHandler BuildHandler(TelegramBotDbContext db) =>
        new(
            new ChatBindingRepository(db),
            ChatApi,
            BuildTransactionManager(db),
            NullLogger<PlanHardDeletedTelegramHandler>.Instance);

    private async Task SeedChatBindingAsync(Guid planId, long telegramChatId) =>
        await ExecuteInDb(async db =>
        {
            ChatBinding b = ChatBinding.Create(
                Guid.NewGuid(), planId, telegramChatId, ChatType.SUPERGROUP,
                chatTitle: "Plan chat", inviteLink: "https://t.me/+abc",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });
}
