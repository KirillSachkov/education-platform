using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NotificationService.Contracts.WebPush.Requests;
using NotificationService.Domain.WebPush;
using NotificationService.IntegrationTests.Infrastructure;

namespace NotificationService.IntegrationTests.Features.WebPush;

/// <summary>
/// POST/DELETE /notifications/push/subscriptions — регистрация/отписка устройства (#342).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class WebPushSubscriptionEndpointsTests : NotificationServiceTestsBase
{
    public WebPushSubscriptionEndpointsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Register_PersistsSubscription()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId);

        RegisterWebPushSubscriptionRequest req = new()
        {
            Endpoint = "https://push.example.com/sub/abc",
            P256dh = "test-p256dh-key",
            Auth = "test-auth-secret",
            UserAgent = "Mozilla/5.0 Test",
        };

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/notifications/push/subscriptions", req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        List<WebPushSubscription> rows = await ExecuteInDb(db => db.WebPushSubscriptions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync());

        Assert.Single(rows);
        Assert.Equal(req.Endpoint, rows[0].Endpoint);
        Assert.Equal(req.P256dh, rows[0].P256dh);
        Assert.Equal(req.Auth, rows[0].Auth);
    }

    [Fact]
    public async Task Register_SameEndpointTwice_IsIdempotentUpsert()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId);

        const string endpoint = "https://push.example.com/sub/dup";

        await AppHttpClient.PostAsJsonAsync("/notifications/push/subscriptions",
            new RegisterWebPushSubscriptionRequest { Endpoint = endpoint, P256dh = "k1", Auth = "a1" });
        await AppHttpClient.PostAsJsonAsync("/notifications/push/subscriptions",
            new RegisterWebPushSubscriptionRequest { Endpoint = endpoint, P256dh = "k2", Auth = "a2" });

        List<WebPushSubscription> rows = await ExecuteInDb(db => db.WebPushSubscriptions
            .AsNoTracking()
            .Where(x => x.Endpoint == endpoint)
            .ToListAsync());

        Assert.Single(rows);            // upsert, не дубликат
        Assert.Equal("k2", rows[0].P256dh); // обновлён последним
    }

    [Fact]
    public async Task Register_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/notifications/push/subscriptions",
            new RegisterWebPushSubscriptionRequest { Endpoint = "e", P256dh = "k", Auth = "a" });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Remove_DeletesSubscription()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId);

        const string endpoint = "https://push.example.com/sub/remove-me";
        await AppHttpClient.PostAsJsonAsync("/notifications/push/subscriptions",
            new RegisterWebPushSubscriptionRequest { Endpoint = endpoint, P256dh = "k", Auth = "a" });

        HttpRequestMessage delete = new(HttpMethod.Delete, "/notifications/push/subscriptions")
        {
            Content = JsonContent.Create(new RemoveWebPushSubscriptionRequest { Endpoint = endpoint }),
        };
        HttpResponseMessage resp = await AppHttpClient.SendAsync(delete);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        int count = await ExecuteInDb(db => db.WebPushSubscriptions
            .CountAsync(x => x.Endpoint == endpoint));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Remove_OtherUsersEndpoint_DoesNotDelete()
    {
        Guid owner = Guid.NewGuid();
        Guid attacker = Guid.NewGuid();
        const string endpoint = "https://push.example.com/sub/owned";

        AuthenticateAs(owner);
        await AppHttpClient.PostAsJsonAsync("/notifications/push/subscriptions",
            new RegisterWebPushSubscriptionRequest { Endpoint = endpoint, P256dh = "k", Auth = "a" });

        AuthenticateAs(attacker);
        HttpRequestMessage delete = new(HttpMethod.Delete, "/notifications/push/subscriptions")
        {
            Content = JsonContent.Create(new RemoveWebPushSubscriptionRequest { Endpoint = endpoint }),
        };
        HttpResponseMessage resp = await AppHttpClient.SendAsync(delete);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // идемпотентно, не раскрывает существование

        int count = await ExecuteInDb(db => db.WebPushSubscriptions
            .CountAsync(x => x.Endpoint == endpoint));
        Assert.Equal(1, count); // чужая подписка цела
    }
}
