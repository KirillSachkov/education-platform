using System.Net;
using System.Net.Http.Json;
using NotificationService.Contracts.Inbox.Dtos;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.Inbox;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class ListMyNotificationsTests : NotificationServiceTestsBase
{
    public ListMyNotificationsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetNotifications_ReturnsInboxForCurrentUser()
    {
        Guid userId = Guid.NewGuid();

        await SeedNotificationsAsync(userId, count: 5);

        AuthenticateAs(userId, "platform-participant");

        NotificationListResponse response = await GetNotificationsAsync("/notifications?limit=20");

        Assert.Equal(5, response.Items.Count);
    }

    [Fact]
    public async Task GetNotifications_FilterByTypes_ReturnsOnlyMatching()
    {
        Guid userId = Guid.NewGuid();
        await SeedMixedTypesAsync(userId);

        AuthenticateAs(userId, "platform-participant");

        // Filter to comment-related types only (10 = CommentReplied, 11 = CommentOnOwnContent).
        NotificationListResponse response = await GetNotificationsAsync(
            $"/notifications?limit=20&types={(short)NotificationType.CommentReplied}&types={(short)NotificationType.CommentOnOwnContent}");

        Assert.Equal(2, response.Items.Count);
        Assert.All(response.Items, item =>
            Assert.True(
                item.Type == (short)NotificationType.CommentReplied
                || item.Type == (short)NotificationType.CommentOnOwnContent));
    }

    [Fact]
    public async Task GetNotifications_NoTypesFilter_ReturnsAllTypes()
    {
        Guid userId = Guid.NewGuid();
        await SeedMixedTypesAsync(userId);

        AuthenticateAs(userId, "platform-participant");

        NotificationListResponse response = await GetNotificationsAsync("/notifications?limit=20");

        Assert.Equal(4, response.Items.Count);
    }

    [Fact]
    public async Task UnreadCount_ReflectsReadStatus()
    {
        Guid userId = Guid.NewGuid();

        List<Notification> seeded = await SeedNotificationsAsync(userId, count: 5);

        AuthenticateAs(userId, "platform-participant");

        // Initially all 5 unread.
        int unreadInitial = await ReadUnreadCountAsync();
        Assert.Equal(5, unreadInitial);

        // Mark one as read.
        HttpResponseMessage markResp = await AppHttpClient.PostAsync(
            new Uri($"/notifications/{seeded[0].Id.Value}/read", UriKind.Relative),
            content: null);
        Assert.Equal(HttpStatusCode.OK, markResp.StatusCode);

        int unreadAfter = await ReadUnreadCountAsync();
        Assert.Equal(4, unreadAfter);
    }

    private async Task<NotificationListResponse> GetNotificationsAsync(string relativeUrl)
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri(relativeUrl, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<NotificationListResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<NotificationListResponse>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        return envelope.Result!;
    }

    private async Task<int> ReadUnreadCountAsync()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri("/notifications/unread-count", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<UnreadCountResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<UnreadCountResponse>>();
        Assert.NotNull(envelope);
        return envelope.Result!.Count;
    }

    private async Task SeedMixedTypesAsync(Guid userId)
    {
        NotificationType[] types =
        [
            NotificationType.Welcome,             // 1 — Account
            NotificationType.CourseEnrolled,      // 2 — Course
            NotificationType.CommentReplied,      // 10 — Comments
            NotificationType.CommentOnOwnContent, // 11 — Comments
        ];

        await ExecuteInDb(async db =>
        {
            int idx = 0;
            foreach (NotificationType t in types)
            {
                Notification n = Notification.Create(
                    recipientUserId: userId,
                    type: t,
                    templateId: $"t.{(short)t}",
                    title: $"Title {idx}",
                    body: $"Body {idx}",
                    channels: NotificationChannel.InApp,
                    payload: "{}",
                    correlationId: Guid.NewGuid()).Value;

                await db.Notifications.AddAsync(n);
                idx++;
            }

            await db.SaveChangesAsync();
        });
    }

    private async Task<List<Notification>> SeedNotificationsAsync(Guid userId, int count)
    {
        List<Notification> created = [];
        await ExecuteInDb(async db =>
        {
            for (int i = 0; i < count; i++)
            {
                Notification n = Notification.Create(
                    recipientUserId: userId,
                    type: NotificationType.Welcome,
                    templateId: "welcome",
                    title: $"Title {i}",
                    body: $"Body {i}",
                    channels: NotificationChannel.InApp,
                    payload: "{}",
                    correlationId: Guid.NewGuid()).Value;

                await db.Notifications.AddAsync(n);
                created.Add(n);
            }

            await db.SaveChangesAsync();
        });

        return created;
    }
}
