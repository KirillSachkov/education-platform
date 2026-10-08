using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для <c>plan_grant.expired</c> (#687): time-limited grant истёк по TTL →
/// самому пользователю приходит одно уведомление «Доступ закончился» с описанием истёкшего
/// доступа (из <c>PlanTier</c> — событие не несёт PlanName) и каналами InApp + Telegram + Email.
/// Идемпотентность per-grant через <c>GrantId</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class AccessExpiredHandlerTests : NotificationServiceTestsBase
{
    public AccessExpiredHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AccessExpired_NotifiesUser_OnAllThreeChannels()
    {
        Guid userId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new PlanGrantExpired(
            GrantId: grantId,
            UserId: userId,
            PlanId: Guid.NewGuid(),
            PlanTier: PlanTierNames.FULL_ALL,
            PlanAuthorId: Guid.NewGuid(),
            CourseId: null,
            ExpiredAt: DateTimeOffset.UtcNow));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId));

        Assert.Equal(NotificationType.AccessExpired, notification.Type);
        Assert.Equal(grantId, notification.CorrelationId);

        // Владелец явно хочет все три канала — для юзера без записи в user_notification_channels
        // dispatcher использует дефолтную маску (InApp | Telegram | Email), а Email-канал и
        // force-Telegram зарегистрированы, поэтому в Channels оседают все три.
        Assert.True(notification.Channels.HasFlag(NotificationChannel.InApp));
        Assert.True(notification.Channels.HasFlag(NotificationChannel.Telegram));
        Assert.True(notification.Channels.HasFlag(NotificationChannel.Email));
    }

    [Theory]
    [InlineData(PlanTierNames.FULL_ALL, "Срок полного доступа к .NET Fullstack")]
    [InlineData(PlanTierNames.LEARN_ALL, "Срок доступа ко всем материалам .NET Fullstack")]
    [InlineData(PlanTierNames.COURSE, "Срок доступа к курсу")]
    [InlineData("SOMETHING_UNKNOWN", "Срок вашего доступа")]
    public async Task AccessExpired_BuildsAccessSummary_FromPlanTier(string planTier, string expectedSummary)
    {
        Guid userId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new PlanGrantExpired(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: Guid.NewGuid(),
            PlanTier: planTier,
            PlanAuthorId: Guid.NewGuid(),
            CourseId: null,
            ExpiredAt: DateTimeOffset.UtcNow));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId));

        Assert.Contains(expectedSummary, notification.Body, StringComparison.Ordinal);
        Assert.Contains("истёк", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccessExpired_Twice_IsIdempotent()
    {
        Guid userId = Guid.NewGuid();

        PlanGrantExpired evt = new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: Guid.NewGuid(),
            PlanTier: PlanTierNames.COURSE,
            PlanAuthorId: Guid.NewGuid(),
            CourseId: Guid.NewGuid(),
            ExpiredAt: DateTimeOffset.UtcNow);

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == userId));

        Assert.Equal(1, count);
    }
}
