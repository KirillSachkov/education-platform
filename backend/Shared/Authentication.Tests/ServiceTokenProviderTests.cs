using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PlatformAuth.HttpClients;

namespace PlatformAuth.Tests;

public class ServiceTokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_PropagatesCallerCancellation()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            requestStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using ServiceTokenProvider provider = CreateProvider(handler);
        using var cancellation = new CancellationTokenSource();
        Task request = provider.GetTokenAsync(cancellation.Token);
        await requestStarted.Task;
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => request);
    }

    [Fact]
    public async Task GetTokenAsync_DoesNotRetryPermanentClientError()
    {
        var handler = new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("invalid_client", Encoding.UTF8, "text/plain")
            }));
        using ServiceTokenProvider provider = CreateProvider(handler);

        var result = await provider.GetTokenAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetTokenAsync_RejectsNonPositiveExpiry()
    {
        var handler = new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"token","expires_in":0,"token_type":"Bearer"}""",
                    Encoding.UTF8,
                    "application/json")
            }));
        using ServiceTokenProvider provider = CreateProvider(handler);

        var result = await provider.GetTokenAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetTokenAsync_RetriesTransientServerErrors()
    {
        int attempt = 0;
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            Interlocked.Increment(ref attempt) < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"token","expires_in":300,"token_type":"Bearer"}""",
                        Encoding.UTF8,
                        "application/json")
                }));
        using ServiceTokenProvider provider = CreateProvider(handler);

        var result = await provider.GetTokenAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, handler.RequestCount);
    }

    private static ServiceTokenProvider CreateProvider(HttpMessageHandler handler)
    {
        var options = Options.Create(new ServiceClientOptions
        {
            TokenUrl = "https://auth.test/connect/token/",
            ClientId = "service",
            ClientSecret = "secret"
        });

        return new ServiceTokenProvider(
            new TestHttpClientFactory(handler),
            options,
            Substitute.For<ILogger<ServiceTokenProvider>>());
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return send(request, cancellationToken);
        }
    }
}
