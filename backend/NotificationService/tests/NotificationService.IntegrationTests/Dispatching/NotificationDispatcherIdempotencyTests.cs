using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Notifications;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;

namespace NotificationService.IntegrationTests.Dispatching;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class NotificationDispatcherIdempotencyTests : NotificationServiceTestsBase
{
    public NotificationDispatcherIdempotencyTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Duplicate_request_should_not_block_later_new_request_in_same_batch()
    {
        Guid userId = Guid.CreateVersion7();
        NotificationRequest duplicate = NotificationRequest.From(
            NotificationTemplates.LinkAccountsNudge,
            userId,
            correlationId: Guid.CreateVersion7());
        NotificationRequest newRequest = NotificationRequest.From(
            NotificationTemplates.LinkAccountsNudge,
            userId,
            correlationId: Guid.CreateVersion7());

        await DispatchAsync([duplicate]);
        await DispatchAsync([duplicate, newRequest]);

        int count = await ExecuteInDb(db => db.Notifications.CountAsync(
            notification => notification.RecipientUserId == userId
                            && notification.Type == NotificationType.LinkAccountsNudge));
        Assert.Equal(2, count);
    }

    private async Task DispatchAsync(IReadOnlyList<NotificationRequest> requests)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        INotificationDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        await dispatcher.DispatchAsync(requests);
    }
}
