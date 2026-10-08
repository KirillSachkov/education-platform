using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Deliveries;
using NotificationService.Domain.Notifications;
using NotificationService.Infrastructure.Postgres;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Notifications.Events;

namespace NotificationService.IntegrationTests.Features.Telegram;

/// <summary>
/// L1-тесты на handler <c>TelegramDeliveryRecorded</c>: TelegramBotService публикует
/// событие после попытки доставки, NotificationService пишет результат в
/// <c>notification_deliveries</c> с <c>channel = Telegram</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class TelegramDeliveryRecordedHandlerTests : NotificationServiceTestsBase
{
    public TelegramDeliveryRecordedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Delivered_WritesDeliveryRecordWithProviderMessageId()
    {
        Guid notificationId = await SeedNotificationAsync();
        var evt = new TelegramDeliveryRecorded(
            NotificationId: notificationId,
            RecipientUserId: Guid.NewGuid(),
            ChatId: 12345,
            Status: TelegramDeliveryStatuses.DELIVERED,
            ProviderMessageId: "777",
            ErrorCode: null,
            ErrorDetail: null,
            RecordedAt: DateTimeOffset.UtcNow);

        await InvokeMessageAndWaitAsync(evt);

        NotificationDelivery? delivery = await ExecuteInDb(db =>
            db.NotificationDeliveries.SingleOrDefaultAsync(d =>
                d.NotificationId == NotificationId.Of(notificationId)
                && d.Channel == NotificationChannel.Telegram));

        Assert.NotNull(delivery);
        Assert.Equal(DeliveryStatus.Delivered, delivery!.Status);
        Assert.Equal("777", delivery.ProviderMessageId);
        Assert.Null(delivery.ErrorCode);
    }

    [Fact]
    public async Task Failed_BotBlocked_WritesFailedDeliveryWithErrorCode()
    {
        Guid notificationId = await SeedNotificationAsync();
        var evt = new TelegramDeliveryRecorded(
            NotificationId: notificationId,
            RecipientUserId: Guid.NewGuid(),
            ChatId: 23456,
            Status: TelegramDeliveryStatuses.FAILED,
            ProviderMessageId: null,
            ErrorCode: TelegramDeliveryErrorCodes.BOT_BLOCKED,
            ErrorDetail: "Forbidden: bot was blocked",
            RecordedAt: DateTimeOffset.UtcNow);

        await InvokeMessageAndWaitAsync(evt);

        NotificationDelivery? delivery = await ExecuteInDb(db =>
            db.NotificationDeliveries.SingleOrDefaultAsync(d =>
                d.NotificationId == NotificationId.Of(notificationId)
                && d.Channel == NotificationChannel.Telegram));

        Assert.NotNull(delivery);
        Assert.Equal(DeliveryStatus.Failed, delivery!.Status);
        Assert.Equal(TelegramDeliveryErrorCodes.BOT_BLOCKED, delivery.ErrorCode);
        Assert.Equal("Forbidden: bot was blocked", delivery.ErrorDetail);
    }

    [Fact]
    public async Task Skipped_NoUserLink_WritesSkippedDeliveryWithErrorCode()
    {
        Guid notificationId = await SeedNotificationAsync();
        var evt = new TelegramDeliveryRecorded(
            NotificationId: notificationId,
            RecipientUserId: Guid.NewGuid(),
            ChatId: 0,
            Status: TelegramDeliveryStatuses.SKIPPED,
            ProviderMessageId: null,
            ErrorCode: TelegramDeliveryErrorCodes.NO_USER_LINK,
            ErrorDetail: null,
            RecordedAt: DateTimeOffset.UtcNow);

        await InvokeMessageAndWaitAsync(evt);

        NotificationDelivery? delivery = await ExecuteInDb(db =>
            db.NotificationDeliveries.SingleOrDefaultAsync(d =>
                d.NotificationId == NotificationId.Of(notificationId)
                && d.Channel == NotificationChannel.Telegram));

        Assert.NotNull(delivery);
        Assert.Equal(DeliveryStatus.Skipped, delivery!.Status);
        // После фикса MarkSkipped error_code пишется в собственную колонку, не в error_detail.
        // GROUP BY error_code теперь работает для skipped-записей (метрики, алертинг).
        Assert.Equal(TelegramDeliveryErrorCodes.NO_USER_LINK, delivery.ErrorCode);
        Assert.Null(delivery.ErrorDetail);
    }

    [Fact]
    public async Task SecondCall_SameNotification_UpdatesExistingRecordIdempotently()
    {
        // Wolverine retry того же event'a: handler должен upsert'ить, не создавать дубль.
        Guid notificationId = await SeedNotificationAsync();

        var first = new TelegramDeliveryRecorded(
            NotificationId: notificationId,
            RecipientUserId: Guid.NewGuid(),
            ChatId: 34567,
            Status: TelegramDeliveryStatuses.FAILED,
            ProviderMessageId: null,
            ErrorCode: TelegramDeliveryErrorCodes.FLOOD_CONTROL,
            ErrorDetail: "429",
            RecordedAt: DateTimeOffset.UtcNow);

        var second = first with
        {
            Status = TelegramDeliveryStatuses.DELIVERED,
            ProviderMessageId = "888",
            ErrorCode = null,
            ErrorDetail = null,
            RecordedAt = DateTimeOffset.UtcNow.AddSeconds(5)
        };

        await InvokeMessageAndWaitAsync(first);
        await InvokeMessageAndWaitAsync(second);

        List<NotificationDelivery> all = await ExecuteInDb(db =>
            db.NotificationDeliveries
                .Where(d => d.NotificationId == NotificationId.Of(notificationId)
                            && d.Channel == NotificationChannel.Telegram)
                .ToListAsync());

        Assert.Single(all);
        Assert.Equal(DeliveryStatus.Delivered, all[0].Status);
        Assert.Equal("888", all[0].ProviderMessageId);
    }

    /// <summary>
    /// Сидит фейковую Notification в БД, чтобы FK <c>notification_deliveries → notifications</c>
    /// не блокировал INSERT в delivery handler'е.
    /// </summary>
    private async Task<Guid> SeedNotificationAsync()
    {
        Guid notificationId = Guid.NewGuid();
        await ExecuteInDb(async (NotificationDbContext db) =>
        {
            Notification notification = Notification.Create(
                recipientUserId: Guid.NewGuid(),
                type: NotificationType.MaterialPublished,
                templateId: "material.published",
                title: "Test",
                body: "Test body",
                channels: NotificationChannel.InApp | NotificationChannel.Telegram,
                payload: "{}",
                correlationId: null,
                id: NotificationId.Of(notificationId)).Value;

            await db.Notifications.AddAsync(notification);
            await db.SaveChangesAsync();
        });
        return notificationId;
    }
}
