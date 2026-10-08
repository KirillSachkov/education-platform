using System.Net;
using NotificationService.IntegrationTests.Infrastructure;

namespace NotificationService.IntegrationTests.Features.Auth;

/// <summary>
/// SSE-эндпоинт <c>GET /notifications/stream</c> аутентифицируется по query-параметру
/// <c>access_token</c> — нативный браузерный EventSource не умеет слать Authorization
/// header (#457). Проверяем: валидный токен в query → 200; отсутствие / мусорный токен →
/// 401; и КРИТИЧНО — query-токен принимается ТОЛЬКО на <c>/stream</c>, а не на других ручках.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class SseStreamQueryTokenAuthTests : NotificationServiceTestsBase
{
    public SseStreamQueryTokenAuthTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Stream_ValidAccessTokenInQuery_Authenticates()
    {
        // Нативный EventSource шлёт токен в query, БЕЗ Authorization header.
        RemoveAuthentication();
        string token = TestJwtHelper.GenerateToken(Guid.NewGuid());

        // ResponseHeadersRead — стрим бесконечный, нам достаточно статуса + headers'ов;
        // dispose response'а оборвёт соединение, CTS страхует от зависания.
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        using HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri($"/notifications/stream?access_token={token}", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        // 200 + text/event-stream — auth прошла по query-токену.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Stream_NoToken_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri("/notifications/stream", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stream_GarbageAccessToken_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri("/notifications/stream?access_token=not-a-jwt", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonStreamEndpoint_AccessTokenInQuery_StillRejected()
    {
        // Path-scoping: query-токен валиден ТОЛЬКО на /stream. На остальных ручках
        // header обязателен → query-токен не должен аутентифицировать (иначе утечка
        // токен-в-URL паттерна на все эндпоинты).
        RemoveAuthentication();
        string token = TestJwtHelper.GenerateToken(Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri($"/notifications/unread-count/?access_token={token}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
