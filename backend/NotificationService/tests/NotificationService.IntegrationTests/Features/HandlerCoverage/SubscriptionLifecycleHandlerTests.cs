using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class SubscriptionLifecycleHandlerTests : NotificationServiceTestsBase
{
    public SubscriptionLifecycleHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task RenewalSuccess_CreatesQuietConfirmation_FromEventDates()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset expiresAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

        await InvokeMessageAndWaitAsync(CreateRenewed(userId, expiresAt));

        Notification notification = await SingleForAsync(userId);
        Assert.Equal(NotificationType.SubscriptionRenewed, notification.Type);
        Assert.Equal(NotificationChannel.InApp, notification.Channels);
        Assert.Contains(Format(expiresAt), notification.Body, StringComparison.Ordinal);
        Assert.Contains("продлён", notification.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryScheduled_UsesEventRetryAndGraceDates_WithActionableCopy()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset failedAt = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        // Deliberately not equal to the current T/T+48h policy: this proves the consumer
        // renders event dates instead of reconstructing billing rules locally.
        DateTimeOffset nextRetryAt = new(2026, 8, 10, 3, 45, 0, TimeSpan.Zero);
        DateTimeOffset graceEndsAt = new(2026, 8, 12, 19, 30, 0, TimeSpan.Zero);

        await InvokeMessageAndWaitAsync(CreateFailure(
            userId,
            failedAt,
            nextRetryAt,
            graceEndsAt,
            attempt: 1,
            SubscriptionLifecycleStages.RetryScheduled));

        Notification notification = await SingleForAsync(userId);
        Assert.Equal(NotificationType.SubscriptionRenewalProblem, notification.Type);
        AssertAllChannels(notification);
        Assert.Contains(Format(nextRetryAt), notification.Body, StringComparison.Ordinal);
        Assert.Contains(Format(graceEndsAt), notification.Body, StringComparison.Ordinal);
        Assert.Contains("карту", notification.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("баланс", notification.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryScheduled_AfterFirstAttempt_DoesNotCreateAnotherNotification()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset failedAt = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        await InvokeMessageAndWaitAsync(CreateFailure(
            userId,
            failedAt,
            failedAt.AddDays(1),
            failedAt.AddDays(3),
            attempt: 2,
            SubscriptionLifecycleStages.RetryScheduled));

        int count = await ExecuteInDb(db => db.Notifications.CountAsync(x => x.RecipientUserId == userId));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task TerminalFailure_UsesEventGraceDate_AndExplainsHowToRestoreRenewal()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset failedAt = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset graceEndsAt = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

        await InvokeMessageAndWaitAsync(CreateFailure(
            userId,
            failedAt,
            nextRetryAt: null,
            graceEndsAt,
            attempt: 3,
            SubscriptionLifecycleStages.TerminalFailure));

        Notification notification = await SingleForAsync(userId);
        Assert.Equal(NotificationType.SubscriptionRenewalProblem, notification.Type);
        AssertAllChannels(notification);
        Assert.Contains(Format(graceEndsAt), notification.Body, StringComparison.Ordinal);
        Assert.Contains("автопродление остановлено", notification.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Мои планы", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_CreatesConfirmation_WithoutTelegramNoise()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset accessEndsAt = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        await InvokeMessageAndWaitAsync(CreateCancelled(userId, accessEndsAt));

        Notification notification = await SingleForAsync(userId);
        Assert.Equal(NotificationType.SubscriptionRenewalCancelled, notification.Type);
        Assert.True(notification.Channels.HasFlag(NotificationChannel.InApp));
        Assert.True(notification.Channels.HasFlag(NotificationChannel.Email));
        Assert.False(notification.Channels.HasFlag(NotificationChannel.Telegram));
        Assert.Contains(Format(accessEndsAt), notification.Body, StringComparison.Ordinal);
        Assert.Contains("Мои планы", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateBrokerDelivery_DoesNotDuplicateLifecycleNotifications()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        PlanGrantRenewed renewed = CreateRenewed(userId, now.AddDays(30));
        PlanGrantRenewalFailed retry = CreateFailure(
            userId,
            now,
            now.AddDays(1),
            now.AddDays(4),
            1,
            SubscriptionLifecycleStages.RetryScheduled);
        PlanGrantRenewalFailed terminal = CreateFailure(
            userId,
            now.AddDays(3),
            null,
            now.AddDays(4),
            3,
            SubscriptionLifecycleStages.TerminalFailure);
        PlanGrantRenewalCancelled cancelled = CreateCancelled(userId, now.AddDays(30));

        await InvokeMessageAndWaitAsync(renewed);
        await InvokeMessageAndWaitAsync(renewed);
        await InvokeMessageAndWaitAsync(retry);
        await InvokeMessageAndWaitAsync(retry);
        await InvokeMessageAndWaitAsync(terminal);
        await InvokeMessageAndWaitAsync(terminal);
        await InvokeMessageAndWaitAsync(cancelled);
        await InvokeMessageAndWaitAsync(cancelled);

        int count = await ExecuteInDb(db => db.Notifications.CountAsync(x => x.RecipientUserId == userId));
        Assert.Equal(4, count);
    }

    private async Task<Notification> SingleForAsync(Guid userId) =>
        await ExecuteInDb(db => db.Notifications.AsNoTracking().SingleAsync(x => x.RecipientUserId == userId));

    private static PlanGrantRenewed CreateRenewed(Guid userId, DateTimeOffset expiresAt) => new(
        GrantId: Guid.NewGuid(),
        UserId: userId,
        PlanId: Guid.NewGuid(),
        PlanTier: PlanTierNames.SUBSCRIPTION,
        PlanAuthorId: Guid.NewGuid(),
        ExpiresAt: expiresAt,
        RenewalOrderId: Guid.NewGuid(),
        RenewedAt: expiresAt.AddDays(-30),
        PreviousExpiresAt: expiresAt.AddDays(-30),
        NextChargeAt: expiresAt.AddDays(-1),
        GraceEndsAt: expiresAt.AddDays(3),
        Attempt: 1,
        Stage: SubscriptionLifecycleStages.Renewed);

    private static PlanGrantRenewalFailed CreateFailure(
        Guid userId,
        DateTimeOffset failedAt,
        DateTimeOffset? nextRetryAt,
        DateTimeOffset graceEndsAt,
        int attempt,
        string stage) => new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: Guid.NewGuid(),
            PlanTier: PlanTierNames.SUBSCRIPTION,
            PlanAuthorId: Guid.NewGuid(),
            ExpiresAt: graceEndsAt.AddDays(-3),
            FailureCount: attempt,
            FailureReason: "charge.declined",
            RenewalOrderId: Guid.NewGuid(),
            FailedAt: failedAt,
            NextRetryAt: nextRetryAt,
            GraceEndsAt: graceEndsAt,
            Stage: stage);

    private static PlanGrantRenewalCancelled CreateCancelled(Guid userId, DateTimeOffset accessEndsAt) => new(
        GrantId: Guid.NewGuid(),
        UserId: userId,
        PlanId: Guid.NewGuid(),
        PlanTier: PlanTierNames.SUBSCRIPTION,
        PlanAuthorId: Guid.NewGuid(),
        RenewalOrderId: Guid.NewGuid(),
        CancelledAt: accessEndsAt.AddDays(-10),
        AccessEndsAt: accessEndsAt,
        Attempt: 0,
        CorrelationId: Guid.NewGuid());

    private static string Format(DateTimeOffset value) =>
        value.ToString("dd.MM.yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static void AssertAllChannels(Notification notification)
    {
        Assert.True(notification.Channels.HasFlag(NotificationChannel.InApp));
        Assert.True(notification.Channels.HasFlag(NotificationChannel.Telegram));
        Assert.True(notification.Channels.HasFlag(NotificationChannel.Email));
    }
}
