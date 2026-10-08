using System.Net;
using System.Net.Http.Json;
using NotificationService.Contracts.Preferences.Requests;
using NotificationService.Contracts.Subscriptions.Requests;
using NotificationService.IntegrationTests.Infrastructure;

namespace NotificationService.IntegrationTests.Features.Auth;

/// <summary>
/// Negative-auth coverage: каждый user-facing endpoint должен возвращать 401 для
/// анонимного caller'а. Base class по умолчанию ставит admin-токен через
/// <c>AuthenticateAsAdmin</c> — здесь снимаем токен и проверяем 401 на всех ручках.
/// Issue #230, TEST-1.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class AnonymousAccessTests : NotificationServiceTestsBase
{
    public AnonymousAccessTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ListNotifications_AnonymousCaller_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri("/notifications/?limit=10", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnreadCount_AnonymousCaller_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri("/notifications/unread-count/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkAsRead_AnonymousCaller_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            new Uri($"/notifications/{Guid.NewGuid()}/read/", UriKind.Relative),
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkAllAsRead_AnonymousCaller_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            new Uri("/notifications/read-all/", UriKind.Relative),
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPreferences_AnonymousCaller_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri("/notifications/preferences/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePreferences_AnonymousCaller_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            new Uri("/notifications/preferences/", UriKind.Relative),
            new UpdatePreferencesRequest
            {
                TelegramEnabled = true,
                EmailEnabled = true,
                OptedOutTypes = [],
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListSubscriptions_AnonymousCaller_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri("/notifications/subscriptions/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Subscribe_AnonymousCaller_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            new Uri("/notifications/subscriptions/", UriKind.Relative),
            new SubscribeRequest
            {
                EntityType = "course",
                EntityId = Guid.NewGuid(),
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unsubscribe_AnonymousCaller_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            new Uri($"/notifications/subscriptions/{Guid.NewGuid()}/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
