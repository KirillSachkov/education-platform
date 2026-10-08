using System.Net;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shared.AI;
using Shared.AI.OpenAiCompatible;
using SharedKernel;

namespace AI.OpenAiCompatible.Tests;

public sealed class OpenAiCompatibleEmbeddingsClientTests
{
    private const string BASE_URL = "https://api.polza.ai/api/v1/";
    private const string API_KEY = "test-key";

    [Fact]
    public async Task EmbedAsync_HappyPath_ReturnsVectorsAndTokens()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
        {
          "data": [
            {"index": 0, "embedding": [0.1, 0.2, 0.3]},
            {"index": 1, "embedding": [0.4, 0.5, 0.6]}
          ],
          "usage": {"prompt_tokens": 7, "total_tokens": 7},
          "model": "text-embedding-3-small"
        }
        """);
        OpenAiCompatibleEmbeddingsClient sut = CreateSut(handler);

        Result<AiEmbeddingsResult, Error> result = await sut.EmbedAsync(new AiEmbeddingsRequest
        {
            Model = "openai/text-embedding-3-small",
            Inputs = new[] { "hello", "world" },
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Embeddings.Count);
        Assert.Equal(0.1f, result.Value.Embeddings[0].Vector[0]);
        Assert.Equal(0.6f, result.Value.Embeddings[1].Vector[2]);
        Assert.Equal(7, result.Value.InputTokens);
        Assert.Equal("text-embedding-3-small", result.Value.ModelUsed);
        Assert.NotNull(handler.LastRequest);
        Assert.EndsWith("embeddings", handler.LastRequest!.RequestUri!.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbedAsync_5xx_ReturnsProviderFailed()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, """{"error":"upstream"}""");
        OpenAiCompatibleEmbeddingsClient sut = CreateSut(handler);

        Result<AiEmbeddingsResult, Error> result = await sut.EmbedAsync(new AiEmbeddingsRequest
        {
            Model = "openai/text-embedding-3-small",
            Inputs = new[] { "hello" },
        });

        Assert.True(result.IsFailure);
        AssertError(result.Error, "ai.provider.failed");
    }

    [Fact]
    public async Task EmbedAsync_Unauthorized_ReturnsProviderUnauthorized()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"error":"bad key"}""");
        OpenAiCompatibleEmbeddingsClient sut = CreateSut(handler);

        Result<AiEmbeddingsResult, Error> result = await sut.EmbedAsync(new AiEmbeddingsRequest
        {
            Model = "openai/text-embedding-3-small",
            Inputs = new[] { "hello" },
        });

        Assert.True(result.IsFailure);
        AssertError(result.Error, "ai.provider.unauthorized");
    }

    [Fact]
    public async Task EmbedAsync_MalformedJson_ReturnsResponseInvalid()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{ broken json");
        OpenAiCompatibleEmbeddingsClient sut = CreateSut(handler);

        Result<AiEmbeddingsResult, Error> result = await sut.EmbedAsync(new AiEmbeddingsRequest
        {
            Model = "openai/text-embedding-3-small",
            Inputs = new[] { "hello" },
        });

        Assert.True(result.IsFailure);
        AssertError(result.Error, "ai.embeddings.response.invalid");
    }

    [Fact]
    public async Task EmbedAsync_EmptyInputs_ReturnsValidationError()
    {
        OpenAiCompatibleEmbeddingsClient sut = CreateSut(new StubHandler(HttpStatusCode.OK, "{}"));

        Result<AiEmbeddingsResult, Error> result = await sut.EmbedAsync(new AiEmbeddingsRequest
        {
            Model = "openai/text-embedding-3-small",
            Inputs = Array.Empty<string>(),
        });

        Assert.True(result.IsFailure);
        AssertError(result.Error, "ai.embeddings.inputs.empty");
    }

    [Fact]
    public async Task EmbedAsync_TooManyInputs_ReturnsValidationError()
    {
        OpenAiCompatibleEmbeddingsClient sut = CreateSut(new StubHandler(HttpStatusCode.OK, "{}"));
        string[] tooMany = Enumerable.Range(0, 65).Select(i => $"input-{i}").ToArray();

        Result<AiEmbeddingsResult, Error> result = await sut.EmbedAsync(new AiEmbeddingsRequest
        {
            Model = "openai/text-embedding-3-small",
            Inputs = tooMany,
        });

        Assert.True(result.IsFailure);
        AssertError(result.Error, "ai.embeddings.inputs.too_many");
    }

    [Fact]
    public async Task EmbedAsync_HttpRequestException_ReturnsProviderFailed()
    {
        var handler = new ThrowingHandler(new HttpRequestException("connection refused"));
        OpenAiCompatibleEmbeddingsClient sut = CreateSut(handler);

        Result<AiEmbeddingsResult, Error> result = await sut.EmbedAsync(new AiEmbeddingsRequest
        {
            Model = "openai/text-embedding-3-small",
            Inputs = new[] { "hello" },
        });

        Assert.True(result.IsFailure);
        AssertError(result.Error, "ai.provider.failed");
    }

    [Fact]
    public async Task EmbedAsync_TimeoutWithoutCallerCancellation_ReturnsProviderTimeout()
    {
        // HttpClient бросает TaskCanceledException при истечении HttpClient.Timeout —
        // этот же тип исключения летит из cooperative cancellation token'а; различение
        // делается по cancellationToken.IsCancellationRequested внутри client'а.
        var handler = new ThrowingHandler(new TaskCanceledException("HttpClient timeout"));
        OpenAiCompatibleEmbeddingsClient sut = CreateSut(handler);

        Result<AiEmbeddingsResult, Error> result = await sut.EmbedAsync(
            new AiEmbeddingsRequest
            {
                Model = "openai/text-embedding-3-small",
                Inputs = new[] { "hello" },
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertError(result.Error, "ai.provider.timeout");
    }

    [Fact]
    public async Task EmbedAsync_CallerCancellation_PropagatesTaskCanceled()
    {
        // Когда отменяет caller (его CancellationToken signaled) — клиент пробрасывает
        // TaskCanceledException, не маскирует её под "timeout".
        var handler = new ThrowingHandler(new TaskCanceledException("caller cancelled"));
        OpenAiCompatibleEmbeddingsClient sut = CreateSut(handler);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => sut.EmbedAsync(
            new AiEmbeddingsRequest
            {
                Model = "openai/text-embedding-3-small",
                Inputs = new[] { "hello" },
            },
            cancellationToken: cts.Token));
    }

    [Fact]
    public async Task EmbedAsync_NoApiKey_ReturnsProviderUnauthorized()
    {
        OpenAiCompatibleEmbeddingsClient sut = CreateSut(
            new StubHandler(HttpStatusCode.OK, "{}"),
            apiKey: string.Empty);

        Result<AiEmbeddingsResult, Error> result = await sut.EmbedAsync(new AiEmbeddingsRequest
        {
            Model = "openai/text-embedding-3-small",
            Inputs = new[] { "hello" },
        });

        Assert.True(result.IsFailure);
        AssertError(result.Error, "ai.provider.unauthorized");
    }

    private static OpenAiCompatibleEmbeddingsClient CreateSut(
        HttpMessageHandler handler,
        string apiKey = API_KEY,
        string baseUrl = BASE_URL)
    {
        var httpClient = new HttpClient(handler);
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var options = new AiOptions
        {
            Kind = "OpenAiCompatible",
            ApiKey = apiKey,
            BaseUrl = baseUrl,
            TimeoutSeconds = 30,
        };

        return new OpenAiCompatibleEmbeddingsClient(
            options,
            factory,
            NullLogger<OpenAiCompatibleEmbeddingsClient>.Instance);
    }

    private static void AssertError(Error error, string expectedCode)
    {
        Assert.NotEmpty(error.Messages);
        Assert.Equal(expectedCode, error.Messages[0].Code);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string _body;

        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(HttpStatusCode code, string body)
        {
            _code = code;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_code)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Если caller signal'нул отмену — уважаем её и бросаем TaskCanceled с этим
            // токеном (зеркалит поведение HttpClient, чтобы тест cooperative cancellation
            // получал .IsCancellationRequested=true в catch-фильтре клиента).
            cancellationToken.ThrowIfCancellationRequested();
            throw _exception;
        }
    }

}
