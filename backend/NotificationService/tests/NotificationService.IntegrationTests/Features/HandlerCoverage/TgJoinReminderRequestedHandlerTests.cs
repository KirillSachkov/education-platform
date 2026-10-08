using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для <c>tg_join.reminder_requested</c> (#616):
/// нудж на вступление в Telegram-группу плана → самому пользователю.
/// Per-стадийный набор каналов: INITIAL → только InApp (F1 invite-DM уже покрывает Telegram),
/// REMINDER_1/REMINDER_2 → InApp + Telegram + Email. Идемпотентность per-(grant, stage).
/// Событие самодостаточно (PlanName в payload), внешних lookup'ов нет.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class TgJoinReminderRequestedHandlerTests : NotificationServiceTestsBase
{
    public TgJoinReminderRequestedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task TgJoinReminder_Initial_CreatesNotification_WithoutEmailChannel()
    {
        Guid userId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new TgJoinReminderRequested(
            UserId: userId,
            PlanId: Guid.NewGuid(),
            GrantId: grantId,
            PlanName: ".NET Fullstack",
            Stage: TgJoinReminderStages.Initial,
            OccurredAt: DateTimeOffset.UtcNow));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId));

        Assert.Equal(NotificationType.TelegramJoinReminder, notification.Type);
        Assert.Contains(".NET Fullstack", notification.Body, StringComparison.Ordinal);

        // INITIAL → InApp only (F1 invite-DM already covers Telegram; no Email spam at T0).
        Assert.True((notification.Channels & NotificationChannel.InApp) != NotificationChannel.None);
        Assert.Equal(NotificationChannel.None, notification.Channels & NotificationChannel.Telegram);
        Assert.Equal(NotificationChannel.None, notification.Channels & NotificationChannel.Email);
    }

    [Fact]
    public async Task TgJoinReminder_Reminder1_CreatesNotification_WithEmailChannel()
    {
        Guid userId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new TgJoinReminderRequested(
            UserId: userId,
            PlanId: Guid.NewGuid(),
            GrantId: Guid.NewGuid(),
            PlanName: "Курс по C#",
            Stage: TgJoinReminderStages.Reminder1,
            OccurredAt: DateTimeOffset.UtcNow));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId));

        Assert.Equal(NotificationType.TelegramJoinReminder, notification.Type);

        // REMINDER_1 → InApp + Telegram + Email.
        Assert.True((notification.Channels & NotificationChannel.InApp) != NotificationChannel.None);
        Assert.True((notification.Channels & NotificationChannel.Telegram) != NotificationChannel.None);
        Assert.True((notification.Channels & NotificationChannel.Email) != NotificationChannel.None);
    }

    [Fact]
    public async Task TgJoinReminder_SameStageTwice_IsIdempotent()
    {
        Guid userId = Guid.NewGuid();

        TgJoinReminderRequested evt = new(
            UserId: userId,
            PlanId: Guid.NewGuid(),
            GrantId: Guid.NewGuid(),
            PlanName: "Курс",
            Stage: TgJoinReminderStages.Reminder1,
            OccurredAt: DateTimeOffset.UtcNow);

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == userId));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task TgJoinReminder_DifferentStages_SameGrant_AreNotDeduped()
    {
        Guid userId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new TgJoinReminderRequested(
            userId, planId, grantId, "Курс", TgJoinReminderStages.Initial, DateTimeOffset.UtcNow));
        await InvokeMessageAndWaitAsync(new TgJoinReminderRequested(
            userId, planId, grantId, "Курс", TgJoinReminderStages.Reminder1, DateTimeOffset.UtcNow));

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == userId));

        Assert.Equal(2, count);
    }
}
