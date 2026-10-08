using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace NotificationService.IntegrationTests.Features.Inbox;

/// <summary>
///     <c>GET /n/{id}</c> — короткий click-through proxy endpoint.
///     Проверяем базовые invariants:
///     <list type="bullet">
///         <item>Endpoint <c>AllowAnonymous</c> — работает без JWT (email/telegram пользователь может
///             быть не залогинен в браузере).</item>
///         <item>Mark-as-read происходит ТОЛЬКО если auth совпадает с recipient. Для anon — пропускается.</item>
///         <item>Unknown <c>id</c> → 302 на root (не 404) — не ломаем UX click-flow.</item>
///     </list>
///
///     Клиент с <c>AllowAutoRedirect = false</c>, чтобы видеть сам 302 (иначе HttpClient улетит
///     за redirect-ом на абсолютный frontend URL из <c>NotificationOptions.FrontendBaseUrl</c>).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class OpenNotificationTests : NotificationServiceTestsBase
{
    private readonly HttpClient _noRedirectClient;

    public OpenNotificationTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        _noRedirectClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    [Fact]
    public async Task Anonymous_Get_RedirectsWithoutMarkingAsRead()
    {
        // Seed: Welcome notification для случайного user'а.
        Guid userId = Guid.NewGuid();
        await InvokeMessageAndWaitAsync(new UserCreated(userId, "anon-tester", "Anon Tester"));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == userId && x.Type == NotificationType.Welcome)
            .SingleAsync());

        Assert.Null(notification.ReadAt); // baseline: unread

        using HttpRequestMessage request = new(HttpMethod.Get, $"/n/{notification.Id.Value}");
        // no Authorization header — anonymous
        HttpResponseMessage response = await _noRedirectClient.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"Expected 302, got {(int)response.StatusCode}");

        // Mark-as-read пропущен (anon): read_at всё ещё NULL.
        Notification after = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(x => x.Id == notification.Id)
            .SingleAsync());
        Assert.Null(after.ReadAt);
    }

    [Fact]
    public async Task UnknownId_RedirectsToRoot_NoError()
    {
        // Удалённая / никогда не существовавшая notification → не 404, а 302 на корень.
        using HttpRequestMessage request = new(HttpMethod.Get, $"/n/{Guid.NewGuid()}");
        HttpResponseMessage response = await _noRedirectClient.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"Expected 302, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task AuthenticatedAsRecipient_MarksAsRead()
    {
        Guid userId = Guid.NewGuid();
        await InvokeMessageAndWaitAsync(new UserCreated(userId, "owner-tester", "Owner Tester"));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == userId && x.Type == NotificationType.Welcome)
            .SingleAsync());

        using HttpRequestMessage request = new(HttpMethod.Get, $"/n/{notification.Id.Value}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtHelper.GenerateToken(userId));
        HttpResponseMessage response = await _noRedirectClient.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"Expected 302, got {(int)response.StatusCode}");

        Notification after = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(x => x.Id == notification.Id)
            .SingleAsync());
        Assert.NotNull(after.ReadAt);
    }

    [Fact]
    public async Task Repeated_click_through_requests_are_rate_limited()
    {
        using HttpClient client = _noRedirectClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtHelper.GenerateToken(Guid.CreateVersion7()));

        HttpResponseMessage? response = null;
        for (int i = 0; i < 31; i++)
        {
            response?.Dispose();
            response = await client.GetAsync($"/n/{Guid.CreateVersion7()}");
        }

        using (response)
        {
            Assert.NotNull(response);
            string body = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.StatusCode == HttpStatusCode.TooManyRequests,
                $"Expected 429, got {(int)response.StatusCode}: {body}");
        }
    }
}
