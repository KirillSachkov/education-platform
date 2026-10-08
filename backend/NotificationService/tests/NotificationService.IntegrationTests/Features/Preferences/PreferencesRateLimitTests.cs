using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NotificationService.Contracts.Preferences.Requests;
using NotificationService.IntegrationTests.Infrastructure;

namespace NotificationService.IntegrationTests.Features.Preferences;

/// <summary>
///     Rate-limit на <c>PUT /notifications/preferences/</c>.
///     Default 10 request'ов в минуту (FixedWindow, partition = userId).
///     Проверяем: 10 запросов успешны, 11-й возвращает 429 с rate-limit error code.
///     Partition scoped на конкретный userId — другие тесты не пересекаются (base class
///     каждый тест идёт под admin userId = <c>TestJwtHelper.GenerateAdminToken</c>, но здесь
///     используем уникальный per-test userId).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class PreferencesRateLimitTests : NotificationServiceTestsBase
{
    public PreferencesRateLimitTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task PutPreferences_Exceeds10PerMinute_Returns429()
    {
        // Уникальный userId → свой partition. 10 попыток должны пройти, 11-я зарежектиться.
        Guid userId = Guid.NewGuid();
        AppHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtHelper.GenerateToken(userId));

        UpdatePreferencesRequest body = new()
        {
            TelegramEnabled = false,
            EmailEnabled = true,
            OptedOutTypes = [],
        };

        for (int i = 0; i < 10; i++)
        {
            HttpResponseMessage ok = await AppHttpClient.PutAsJsonAsync(
                new Uri("/notifications/preferences", UriKind.Relative), body);
            Assert.True(
                ok.StatusCode == HttpStatusCode.OK,
                $"Request #{i + 1} expected 200, got {(int)ok.StatusCode}");
        }

        HttpResponseMessage rejected = await AppHttpClient.PutAsJsonAsync(
            new Uri("/notifications/preferences", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        string bodyText = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("rate_limit", bodyText, StringComparison.OrdinalIgnoreCase);
    }
}
