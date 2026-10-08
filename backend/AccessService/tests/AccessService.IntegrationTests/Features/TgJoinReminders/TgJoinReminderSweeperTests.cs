using AccessService.Domain;
using AccessService.Domain.TgJoinReminders;
using AccessService.IntegrationTests.Infrastructure;
using AccessService.Web.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Messaging.IntegrationEvents.Access.Events;
using TelegramBotService.Contracts.Dtos;

namespace AccessService.IntegrationTests.Features.TgJoinReminders;

/// <summary>
///     Integration tests for <see cref="TgJoinReminderSweeper"/> (#616, ST-3). Seeds a due row
///     (age past the first window) and runs one sweep pass with the fake membership client set to
///     not-member / member / unknown. Verifies REMINDER_1 published + RemindersSent bumped (not-member),
///     reactive completion (member), and reminder NOT burned (unknown / TBS down).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class TgJoinReminderSweeperTests : AccessServiceTestsBase
{
    public TgJoinReminderSweeperTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Due_row_not_member_publishes_reminder1_and_bumps_count()
    {
        Guid planId = await SeedPlanAsync();
        Guid userId = Guid.NewGuid();
        await SeedDueReminderAsync(userId, planId, ageDays: 3);
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "not_member");

        int published = await RunSweeperAsync();

        Assert.Equal(1, published);
        TgJoinReminderRequested evt = OutboxCollector.OfType<TgJoinReminderRequested>().Single();
        Assert.Equal(TgJoinReminderStages.Reminder1, evt.Stage);
        Assert.Equal(userId, evt.UserId);

        await ExecuteInDbAsync(async db =>
        {
            TgJoinReminder row = await db.TgJoinReminders
                .SingleAsync(r => r.UserId == userId && r.PlanId == planId);
            Assert.Equal(1, row.RemindersSent);
            Assert.NotNull(row.LastRemindedAt);
            Assert.Null(row.CompletedAt);
        });
    }

    [Fact]
    public async Task Due_row_member_is_completed_without_publishing()
    {
        Guid planId = await SeedPlanAsync();
        Guid userId = Guid.NewGuid();
        await SeedDueReminderAsync(userId, planId, ageDays: 3);
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: true, Status: "member");

        int published = await RunSweeperAsync();

        Assert.Equal(0, published);
        Assert.Empty(OutboxCollector.OfType<TgJoinReminderRequested>());

        await ExecuteInDbAsync(async db =>
        {
            TgJoinReminder row = await db.TgJoinReminders
                .SingleAsync(r => r.UserId == userId && r.PlanId == planId);
            Assert.Equal(0, row.RemindersSent);
            Assert.NotNull(row.CompletedAt);
        });
    }

    [Fact]
    public async Task Unknown_membership_skips_without_burning_reminder()
    {
        Guid planId = await SeedPlanAsync();
        Guid userId = Guid.NewGuid();
        await SeedDueReminderAsync(userId, planId, ageDays: 3);
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "unknown");

        int published = await RunSweeperAsync();

        Assert.Equal(0, published);
        Assert.Empty(OutboxCollector.OfType<TgJoinReminderRequested>());

        await ExecuteInDbAsync(async db =>
        {
            TgJoinReminder row = await db.TgJoinReminders
                .SingleAsync(r => r.UserId == userId && r.PlanId == planId);
            Assert.Equal(0, row.RemindersSent); // not burned
            Assert.Null(row.CompletedAt);
        });
    }

    [Fact]
    public async Task Tbs_down_skips_without_burning_reminder()
    {
        Guid planId = await SeedPlanAsync();
        Guid userId = Guid.NewGuid();
        await SeedDueReminderAsync(userId, planId, ageDays: 3);
        Factory.TelegramClient.ShouldFail = true;

        int published = await RunSweeperAsync();

        Assert.Equal(0, published);
        Assert.Empty(OutboxCollector.OfType<TgJoinReminderRequested>());

        await ExecuteInDbAsync(async db =>
        {
            TgJoinReminder row = await db.TgJoinReminders
                .SingleAsync(r => r.UserId == userId && r.PlanId == planId);
            Assert.Equal(0, row.RemindersSent);
        });
    }

    [Fact]
    public async Task Not_yet_due_row_is_skipped()
    {
        Guid planId = await SeedPlanAsync();
        Guid userId = Guid.NewGuid();
        await SeedDueReminderAsync(userId, planId, ageDays: 1); // below 2-day first window
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "not_member");

        int published = await RunSweeperAsync();

        Assert.Equal(0, published);
        Assert.Empty(OutboxCollector.OfType<TgJoinReminderRequested>());
    }

    private async Task<int> RunSweeperAsync()
    {
        IOptionsMonitor<TgJoinReminderSweeperOptions> options =
            Factory.Services.GetRequiredService<IOptionsMonitor<TgJoinReminderSweeperOptions>>();
        ILogger<TgJoinReminderSweeper> logger =
            Factory.Services.GetRequiredService<ILogger<TgJoinReminderSweeper>>();

        TgJoinReminderSweeper sweeper = new(Factory.Services, options, logger);
        return await sweeper.SweepOnceAsync(CancellationToken.None);
    }

    private async Task SeedDueReminderAsync(Guid userId, Guid planId, int ageDays)
    {
        await ExecuteInDbAsync(async db =>
        {
            TgJoinReminder row = TgJoinReminder.Create(
                userId, planId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-ageDays));
            db.TgJoinReminders.Add(row);
            await db.SaveChangesAsync();
        });
    }

    private async Task<Guid> SeedPlanAsync()
    {
        Plan plan = Plan.Create(
            Guid.NewGuid(),
            PlanTier.FULL_ALL,
            PlanSlug.Of($"tg-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of("Telegram reminders").Value,
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
