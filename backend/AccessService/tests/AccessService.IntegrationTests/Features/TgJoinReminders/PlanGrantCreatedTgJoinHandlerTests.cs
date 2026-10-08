using AccessService.Domain;
using AccessService.Domain.TgJoinReminders;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.IntegrationEvents.Access.Events;
using Wolverine.Tracking;

namespace AccessService.IntegrationTests.Features.TgJoinReminders;

/// <summary>
///     L1 handler tests for <see cref="PlanGrantCreatedTgJoinHandler"/> (#616, ST-3). Invokes
///     the handler in-process via <c>InvokeMessageAndWaitAsync</c>. Covers gating: community grant
///     w/ chats → tg_join_reminders row created + INITIAL published; MIGRATION / self-grant /
///     no-community-capability / no-chats → nothing; idempotent on repeat.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class PlanGrantCreatedTgJoinHandlerTests : AccessServiceTestsBase
{
    private const string COMMUNITY_ACCESS = "COMMUNITY_ACCESS";

    public PlanGrantCreatedTgJoinHandlerTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Community_grant_with_chats_creates_row_and_publishes_initial()
    {
        Guid planId = await SeedPlanAsync();
        Guid userId = Guid.NewGuid();
        Factory.TelegramClient.ActiveBindingsByPlanId.Add(planId);

        await InvokeAsync(BuildEvent(planId, userId, capabilities: [COMMUNITY_ACCESS]));

        await ExecuteInDbAsync(async db =>
        {
            TgJoinReminder row = await db.TgJoinReminders
                .SingleAsync(r => r.UserId == userId && r.PlanId == planId);
            Assert.Equal(0, row.RemindersSent);
            Assert.Null(row.CompletedAt);
        });

        TgJoinReminderRequested published = OutboxCollector.OfType<TgJoinReminderRequested>().Single();
        Assert.Equal(TgJoinReminderStages.Initial, published.Stage);
        Assert.Equal(userId, published.UserId);
        Assert.Equal(planId, published.PlanId);
    }

    [Fact]
    public async Task Migration_source_does_nothing()
    {
        Guid planId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Factory.TelegramClient.ActiveBindingsByPlanId.Add(planId);

        await InvokeAsync(BuildEvent(
            planId, userId, capabilities: [COMMUNITY_ACCESS],
            source: nameof(PlanGrantSource.MIGRATION)));

        await AssertNoRowOrEvent(planId, userId);
    }

    [Fact]
    public async Task Self_grant_does_nothing()
    {
        Guid planId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Factory.TelegramClient.ActiveBindingsByPlanId.Add(planId);

        // UserId == PlanAuthorId → self-grant.
        await InvokeAsync(BuildEvent(
            planId, userId: authorId, capabilities: [COMMUNITY_ACCESS], planAuthorId: authorId));

        await AssertNoRowOrEvent(planId, authorId);
    }

    [Fact]
    public async Task No_community_capability_does_nothing()
    {
        Guid planId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Factory.TelegramClient.ActiveBindingsByPlanId.Add(planId);

        await InvokeAsync(BuildEvent(planId, userId, capabilities: ["VIEW_MATERIALS"]));

        await AssertNoRowOrEvent(planId, userId);
    }

    [Fact]
    public async Task No_telegram_chats_does_nothing()
    {
        Guid planId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        // Plan NOT added to ActiveBindingsByPlanId → HasActiveChatBinding returns false.

        await InvokeAsync(BuildEvent(planId, userId, capabilities: [COMMUNITY_ACCESS]));

        await AssertNoRowOrEvent(planId, userId);
    }

    [Fact]
    public async Task Is_idempotent_on_repeat()
    {
        Guid planId = await SeedPlanAsync();
        Guid userId = Guid.NewGuid();
        Factory.TelegramClient.ActiveBindingsByPlanId.Add(planId);

        PlanGrantCreated evt = BuildEvent(planId, userId, capabilities: [COMMUNITY_ACCESS]);
        await InvokeAsync(evt);
        OutboxCollector.Clear();
        await InvokeAsync(evt); // second time — row already exists → no-op.

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.TgJoinReminders.CountAsync(r => r.UserId == userId && r.PlanId == planId);
            Assert.Equal(1, count);
        });
        Assert.Empty(OutboxCollector.OfType<TgJoinReminderRequested>());
    }

    private async Task InvokeAsync(PlanGrantCreated evt)
    {
        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(evt);
    }

    private async Task AssertNoRowOrEvent(Guid planId, Guid userId)
    {
        await ExecuteInDbAsync(async db =>
        {
            int count = await db.TgJoinReminders.CountAsync(r => r.UserId == userId && r.PlanId == planId);
            Assert.Equal(0, count);
        });
        Assert.Empty(OutboxCollector.OfType<TgJoinReminderRequested>());
    }

    private static PlanGrantCreated BuildEvent(
        Guid planId,
        Guid userId,
        IReadOnlyList<string> capabilities,
        string? source = null,
        Guid? planAuthorId = null) =>
        new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: planId,
            PlanTier: nameof(PlanTier.FULL_ALL),
            PlanAuthorId: planAuthorId ?? Guid.NewGuid(),
            CourseId: null,
            IncludesFutureContent: true,
            Source: source ?? nameof(PlanGrantSource.PURCHASE),
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Capabilities: capabilities,
            CourseIds: null,
            PlanName: "Полный доступ");

    private async Task<Guid> SeedPlanAsync()
    {
        Plan plan = Plan.Create(
            Guid.NewGuid(),
            PlanTier.FULL_ALL,
            PlanSlug.Of($"tg-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of("Telegram invitation").Value,
            [],
            null).Value;
        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });
        return plan.Id;
    }
}
