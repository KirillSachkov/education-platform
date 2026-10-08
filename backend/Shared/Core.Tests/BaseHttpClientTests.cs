using System.Net;
using System.Text;
using Core.HttpCommunication;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel;

namespace Core.Tests;

public class BaseHttpClientTests
{
    [Fact]
    public async Task GetAsync_DisposesResponseAfterParsing()
    {
        var content = new TrackingContent("""{"result":{"value":"ok"},"error":null}""");
        using HttpResponseMessage response = new(HttpStatusCode.OK) { Content = content };
        using var httpClient = CreateHttpClient(response);
        var client = new TestHttpClient(httpClient);

        Result<TestPayload, Error> result = await client.GetPayloadAsync();

        Assert.True(result.IsSuccess);
        Assert.True(content.IsDisposed);
    }

    [Fact]
    public async Task GetOptionalAsync_DisposesNoContentResponse()
    {
        var content = new TrackingContent(string.Empty);
        using HttpResponseMessage response = new(HttpStatusCode.NoContent) { Content = content };
        using var httpClient = CreateHttpClient(response);
        var client = new TestHttpClient(httpClient);

        Result<TestPayload?, Error> result = await client.GetOptionalPayloadAsync();

        Assert.True(result.IsSuccess);
        Assert.True(content.IsDisposed);
    }

    [Fact]
    public async Task DeleteAsync_DisposesResponseAfterParsing()
    {
        var content = new TrackingContent("""{"result":null,"error":null}""");
        using HttpResponseMessage response = new(HttpStatusCode.OK) { Content = content };
        using var httpClient = CreateHttpClient(response);
        var client = new TestHttpClient(httpClient);

        UnitResult<Error> result = await client.DeletePayloadAsync();

        Assert.True(result.IsSuccess);
        Assert.True(content.IsDisposed);
    }

    private sealed class TestHttpClient(HttpClient httpClient)
        : BaseHttpClient(httpClient, Substitute.For<ILogger>(), "test")
    {
        public Task<Result<TestPayload, Error>> GetPayloadAsync() =>
            GetAsync<TestPayload>("resource/", CancellationToken.None);

        public Task<Result<TestPayload?, Error>> GetOptionalPayloadAsync() =>
            GetOptionalAsync<TestPayload>("resource/", CancellationToken.None);

        public Task<UnitResult<Error>> DeletePayloadAsync() =>
            DeleteAsync("resource/", CancellationToken.None);
    }

    private sealed record TestPayload(string Value);

    private static HttpClient CreateHttpClient(HttpResponseMessage response) =>
        new(new ResponseHandler(response)) { BaseAddress = new Uri("https://service.test/") };

    private sealed class ResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class TrackingContent(string content)
        : StringContent(content, Encoding.UTF8, "application/json")
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
