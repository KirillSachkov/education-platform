using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для <c>trial.expiry_approaching</c> (#580):
/// пробный доступ скоро истекает → самому пользователю приходит одно напоминание
/// «Пробный доступ скоро закончится» с названием плана и датой истечения.
/// Событие самодостаточно (PlanName + ExpiresAt в payload), внешних lookup'ов нет.
/// Идемпотентность per-grant через <c>GrantId</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class TrialExpiryApproachingHandlerTests : NotificationServiceTestsBase
{
    public TrialExpiryApproachingHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task TrialExpiryApproaching_NotifiesUser_WithPlanNameAndDate()
    {
        Guid userId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();
        DateTimeOffset expiresAt = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

        await InvokeMessageAndWaitAsync(new TrialExpiryApproaching(
            GrantId: grantId,
            UserId: userId,
            PlanId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            PlanName: "Месяц за рубль",
            ExpiresAt: expiresAt));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId));

        Assert.Equal(NotificationType.TrialExpiryApproaching, notification.Type);
        Assert.Equal(grantId, notification.CorrelationId);
        Assert.Contains("Месяц за рубль", notification.Body, StringComparison.Ordinal);
        Assert.Contains(
            expiresAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            notification.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrialExpiryApproaching_NowAlsoSendsEmail_KeepingInAppAndTelegram()
    {
        // #687 — владелец захотел, чтобы pre-expiry напоминание уходило ещё и письмом.
        // Каналы InApp + Telegram сохраняются, добавляется Email.
        Guid userId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new TrialExpiryApproaching(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            PlanName: "Месяц за рубль",
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(7)));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId));

        Assert.True(notification.Channels.HasFlag(NotificationChannel.InApp));
        Assert.True(notification.Channels.HasFlag(NotificationChannel.Telegram));
        Assert.True(notification.Channels.HasFlag(NotificationChannel.Email));
    }

    [Fact]
    public async Task TrialExpiryApproaching_Twice_IsIdempotent()
    {
        Guid userId = Guid.NewGuid();

        TrialExpiryApproaching evt = new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            PlanName: "Пробный месяц",
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(3));

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == userId));

        Assert.Equal(1, count);
    }
}
