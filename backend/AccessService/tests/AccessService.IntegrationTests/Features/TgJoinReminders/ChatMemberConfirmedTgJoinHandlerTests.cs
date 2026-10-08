using AccessService.Domain;
using AccessService.Domain.TgJoinReminders;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using Wolverine.Tracking;

namespace AccessService.IntegrationTests.Features.TgJoinReminders;

/// <summary>
///     L1 handler tests for <see cref="ChatMemberConfirmedTgJoinHandler"/> (#616, ST-3) —
///     reactive completion of a tg_join_reminders row when chat membership is confirmed.
///     Sibling on the same <c>chat_member.confirmed</c> queue as the onboarding handler.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class ChatMemberConfirmedTgJoinHandlerTests : AccessServiceTestsBase
{
    public ChatMemberConfirmedTgJoinHandlerTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Marks_existing_reminder_completed()
    {
        Guid planId = await SeedPlanAsync();
        Guid userId = Guid.NewGuid();
        await SeedReminderAsync(userId, planId, DateTimeOffset.UtcNow.AddDays(-1));

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new ChatMemberConfirmed(
            PlatformUserId: userId,
            PlanId: planId,
            TelegramChatId: -100123456,
            OccurredAt: DateTimeOffset.UtcNow));

        await ExecuteInDbAsync(async db =>
        {
            TgJoinReminder row = await db.TgJoinReminders
                .SingleAsync(r => r.UserId == userId && r.PlanId == planId);
            Assert.NotNull(row.CompletedAt);
        });
    }

    [Fact]
    public async Task No_op_when_no_reminder_row()
    {
        Guid planId = Guid.NewGuid();
        Guid userId = Guid.NewGuid(); // no row seeded

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new ChatMemberConfirmed(
            userId, planId, -100123456, DateTimeOffset.UtcNow));

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.TgJoinReminders.CountAsync(r => r.UserId == userId);
            Assert.Equal(0, count); // handler never creates rows
        });
    }

    private async Task SeedReminderAsync(Guid userId, Guid planId, DateTimeOffset createdAt)
    {
        await ExecuteInDbAsync(async db =>
        {
            TgJoinReminder row = TgJoinReminder.Create(userId, planId, Guid.NewGuid(), createdAt);
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
            PlanDisplayName.Of("Telegram confirmation").Value,
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
