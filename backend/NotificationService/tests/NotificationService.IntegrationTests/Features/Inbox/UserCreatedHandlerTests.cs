using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.UserChannels;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace NotificationService.IntegrationTests.Features.Inbox;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class UserCreatedHandlerTests : NotificationServiceTestsBase
{
    public UserCreatedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task UserCreated_ShouldCreateWelcomeNotification()
    {
        Guid userId = Guid.NewGuid();
        UserCreated evt = new(userId, Username: "tester", DisplayName: "Test User");

        await InvokeMessageAndWaitAsync(evt);

        List<Notification> stored = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == userId)
            .ToListAsync());

        Assert.Single(stored);

        Notification notification = stored[0];
        Assert.Equal(NotificationType.Welcome, notification.Type);
        Assert.Equal(userId, notification.RecipientUserId);
        Assert.Equal(userId, notification.CorrelationId);
        Assert.False(notification.ReadAt.HasValue);
        Assert.Contains("Test User", notification.Body, StringComparison.Ordinal);

        UserNotificationChannels? channels = await ExecuteInDb(db => db.UserNotificationChannels
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId));

        Assert.NotNull(channels);
        Assert.True(channels!.TelegramEnabled);
        Assert.True(channels.EmailEnabled);
    }

    [Fact]
    public async Task UserCreated_Twice_ShouldBeIdempotent()
    {
        Guid userId = Guid.NewGuid();
        UserCreated evt = new(userId, Username: "tester", DisplayName: "Test User");

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == userId));

        Assert.Equal(1, count);
    }
}
