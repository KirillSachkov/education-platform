using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.UserChannels;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для Telegram link/unlink (issue #230 TEST-2).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class UserTelegramHandlerTests : NotificationServiceTestsBase
{
    public UserTelegramHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task UserTelegramLinked_EnablesTelegramChannel_AndCreatesWelcomeNotification()
    {
        Guid userId = Guid.NewGuid();

        // Сначала создаём канальную запись с TG=false — имитируем юзера, который раньше
        // отключал TG в настройках.
        await SeedUserChannelsAsync(userId, telegramEnabled: false, emailEnabled: true);

        await InvokeMessageAndWaitAsync(new UserTelegramLinked(
            UserId: userId,
            TelegramUserId: 12345L,
            TelegramUsername: "ivan"));

        UserNotificationChannels channels = await ExecuteInDb(db => db.UserNotificationChannels
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId));

        Assert.True(channels.TelegramEnabled);
        Assert.True(channels.EmailEnabled); // не должны затереть email-флаг

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId
                              && x.Type == NotificationType.TelegramLinked));
        Assert.Equal(userId, notification.CorrelationId);
    }

    [Fact]
    public async Task UserTelegramLinked_Twice_DoesNotCreateDuplicateNotification()
    {
        Guid userId = Guid.NewGuid();
        UserTelegramLinked evt = new(userId, 9L, "ivan");

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == userId
                             && x.Type == NotificationType.TelegramLinked));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task UserTelegramUnlinked_DisablesTelegramChannel_AndCreatesNoNotification()
    {
        Guid userId = Guid.NewGuid();

        await SeedUserChannelsAsync(userId, telegramEnabled: true, emailEnabled: true);

        await InvokeMessageAndWaitAsync(new UserTelegramUnlinked(UserId: userId, TelegramUserId: 12345L));

        UserNotificationChannels channels = await ExecuteInDb(db => db.UserNotificationChannels
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId));

        Assert.False(channels.TelegramEnabled);
        Assert.True(channels.EmailEnabled); // не должны затереть email-флаг

        int notificationCount = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == userId));
        Assert.Equal(0, notificationCount);
    }

    private async Task SeedUserChannelsAsync(Guid userId, bool telegramEnabled, bool emailEnabled)
    {
        await ExecuteInDb(async db =>
        {
            UserNotificationChannels channels = UserNotificationChannels.Create(
                userId, telegramEnabled, emailEnabled).Value;
            await db.UserNotificationChannels.AddAsync(channels);
            await db.SaveChangesAsync();
        });
    }
}
