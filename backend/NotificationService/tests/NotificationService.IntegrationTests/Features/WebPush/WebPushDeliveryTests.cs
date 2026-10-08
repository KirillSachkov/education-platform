using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Channels.WebPush;
using NotificationService.Domain.Deliveries;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.UserChannels;
using NotificationService.Domain.WebPush;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace NotificationService.IntegrationTests.Features.WebPush;

/// <summary>
/// Доставка через WebPush-канал (#342, success criterion). Дисpatch уведомления при
/// наличии активной подписки → <c>IWebPushSender.SendAsync</c> вызван + строка
/// <c>notification_deliveries</c> с channel=WebPush. Покрывает gate'ы: отключённый канал,
/// отсутствие подписки, prune протухших endpoint'ов.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class WebPushDeliveryTests : NotificationServiceTestsBase
{
    private readonly IntegrationTestsWebFactory _factory;

    public WebPushDeliveryTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dispatch_WithActiveSubscription_DeliversWebPush()
    {
        Guid userId = Guid.NewGuid();
        const string endpoint = "https://push.example.com/sub/deliver";
        await SeedSubscriptionAsync(userId, endpoint);

        await InvokeMessageAndWaitAsync(new UserCreated(userId, "tester", "Test User"));

        // Канал задействован для этого устройства.
        FakeWebPushSender.SentPush sent = Assert.Single(_factory.WebPushSender.Sent);
        Assert.Equal(endpoint, sent.Endpoint);
        Assert.Contains("\"title\"", sent.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"url\"", sent.PayloadJson, StringComparison.Ordinal);

        // Аудит-лог: строка доставки channel=WebPush, status=Delivered.
        List<NotificationDelivery> deliveries = await ExecuteInDb(db => db.NotificationDeliveries
            .AsNoTracking()
            .Where(d => d.Channel == NotificationChannel.WebPush)
            .ToListAsync());

        NotificationDelivery delivery = Assert.Single(deliveries);
        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
    }

    [Fact]
    public async Task Dispatch_WebPushDisabled_DoesNotDeliver()
    {
        Guid userId = Guid.NewGuid();
        await SeedChannelsAsync(userId, webPushEnabled: false);
        await SeedSubscriptionAsync(userId, "https://push.example.com/sub/disabled");

        await InvokeMessageAndWaitAsync(new UserCreated(userId, "tester", "Test User"));

        Assert.Empty(_factory.WebPushSender.Sent);
        int webPushDeliveries = await ExecuteInDb(db => db.NotificationDeliveries
            .CountAsync(d => d.Channel == NotificationChannel.WebPush));
        Assert.Equal(0, webPushDeliveries);
    }

    [Fact]
    public async Task Dispatch_NoSubscription_DoesNotDeliverWebPush()
    {
        Guid userId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new UserCreated(userId, "tester", "Test User"));

        Assert.Empty(_factory.WebPushSender.Sent);
        int webPushDeliveries = await ExecuteInDb(db => db.NotificationDeliveries
            .CountAsync(d => d.Channel == NotificationChannel.WebPush));
        Assert.Equal(0, webPushDeliveries);
    }

    [Fact]
    public async Task Dispatch_GoneEndpoint_PrunesSubscription()
    {
        Guid userId = Guid.NewGuid();
        const string endpoint = "https://push.example.com/sub/gone";
        await SeedSubscriptionAsync(userId, endpoint);
        _factory.WebPushSender.Outcome = WebPushSendOutcome.Gone;

        await InvokeMessageAndWaitAsync(new UserCreated(userId, "tester", "Test User"));

        // Попытка была, но endpoint протух → подписка удалена.
        Assert.Single(_factory.WebPushSender.Sent);
        int remaining = await ExecuteInDb(db => db.WebPushSubscriptions
            .CountAsync(x => x.Endpoint == endpoint));
        Assert.Equal(0, remaining);
    }

    private async Task SeedSubscriptionAsync(Guid userId, string endpoint)
    {
        WebPushSubscription sub = WebPushSubscription
            .Create(userId, endpoint, "p256dh-key", "auth-secret", "test-ua").Value;
        await ExecuteInDb(async db =>
        {
            await db.WebPushSubscriptions.AddAsync(sub);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedChannelsAsync(Guid userId, bool webPushEnabled)
    {
        UserNotificationChannels channels = UserNotificationChannels
            .Create(userId, telegramEnabled: true, emailEnabled: true, webPushEnabled: webPushEnabled).Value;
        await ExecuteInDb(async db =>
        {
            await db.UserNotificationChannels.AddAsync(channels);
            await db.SaveChangesAsync();
        });
    }
}
