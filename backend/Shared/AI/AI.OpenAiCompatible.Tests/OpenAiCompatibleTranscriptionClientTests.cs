using System.Net;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.AI;
using Shared.AI.OpenAiCompatible;
using SharedKernel;

namespace AI.OpenAiCompatible.Tests;

public sealed class OpenAiCompatibleTranscriptionClientTests
{
    [Fact]
    public async Task TranscribeAsync_ReturnsInputRequired_WhenAudioIsEmpty()
    {
        using ServiceProvider serviceProvider = CreateServiceProvider();
        IAiTranscriptionClient client = serviceProvider.GetRequiredService<IAiTranscriptionClientFactory>().Get(null);

        Result<AiTranscriptionResult, Error> result = await client.TranscribeAsync(
            CreateRequest(audioBytes: []),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Messages, m => m.Code == "ai.input.required");
    }

    [Fact]
    public async Task TranscribeAsync_ReturnsAudioTooLarge_WhenAudioExceedsLimit()
    {
        using ServiceProvider serviceProvider = CreateServiceProvider();
        IAiTranscriptionClient client = serviceProvider.GetRequiredService<IAiTranscriptionClientFactory>().Get(null);
        // 25 МБ + 1 byte — превышает задокументированный Polza/OpenAI лимит.
        // Fail-fast здесь даёт понятный error code вместо непредсказуемого
        // 413/503/timeout от провайдера.
        byte[] tooLarge = new byte[(25 * 1024 * 1024) + 1];

        Result<AiTranscriptionResult, Error> result = await client.TranscribeAsync(
            CreateRequest(audioBytes: tooLarge),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Messages, m => m.Code == "ai.transcription.audio.too_large");
    }

    [Fact]
    public async Task TranscribeAsync_AcceptsAudioAtExactLimit()
    {
        // Граничный случай — ровно 25 МБ должно проходить pre-check (упадёт уже
        // на network — нет реального сервера в тестах, но code path должен дойти).
        using ServiceProvider serviceProvider = CreateServiceProvider(
            baseUrl: "http://127.0.0.1:1/v1/");
        IAiTranscriptionClient client = serviceProvider.GetRequiredService<IAiTranscriptionClientFactory>().Get(null);
        byte[] atLimit = new byte[25 * 1024 * 1024];

        Result<AiTranscriptionResult, Error> result = await client.TranscribeAsync(
            CreateRequest(audioBytes: atLimit, timeoutSeconds: 1),
            CancellationToken.None);

        // Должен пройти pre-check (нет ai.transcription.audio.too_large).
        Assert.True(result.IsFailure);
        Assert.DoesNotContain(result.Error.Messages, m => m.Code == "ai.transcription.audio.too_large");
    }

    [Fact]
    public async Task TranscribeAsync_AcceptsJsonResponseWithoutDuration()
    {
        const string responseBody = """
        {
          "text": "Трассировка связывает логи и спаны.",
          "usage": {
            "type": "tokens",
            "input_tokens": 14,
            "input_token_details": {
              "text_tokens": 0,
              "audio_tokens": 14
            },
            "output_tokens": 8,
            "total_tokens": 22
          }
        }
        """;
        using var httpClient = new HttpClient(new StubHandler(HttpStatusCode.OK, responseBody));
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        var client = new OpenAiCompatibleTranscriptionClient(
            new AiOptions
            {
                ApiKey = "test-api-key",
                BaseUrl = "https://api.example.test/v1/",
                TimeoutSeconds = 30,
            },
            httpClientFactory,
            NullLogger<OpenAiCompatibleTranscriptionClient>.Instance);

        Result<AiTranscriptionResult, Error> result = await client.TranscribeAsync(
            CreateRequest(audioBytes: [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.GetMessage() : null);
        Assert.Equal("Трассировка связывает логи и спаны.", result.Value.FullText);
        Assert.Empty(result.Value.Segments);
        Assert.Equal(0, result.Value.DurationSeconds);
    }

    [Fact]
    public async Task TranscribeAsync_RejectsInvalidTimestampOnBlankSegment()
    {
        const string responseBody = """
        {
          "text": "Распознанный текст",
          "segments": [
            { "start": -1, "end": 1, "text": "" }
          ]
        }
        """;
        using var httpClient = new HttpClient(new StubHandler(HttpStatusCode.OK, responseBody));
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        var client = new OpenAiCompatibleTranscriptionClient(
            new AiOptions
            {
                ApiKey = "test-api-key",
                BaseUrl = "https://api.example.test/v1/",
                TimeoutSeconds = 30,
            },
            httpClientFactory,
            NullLogger<OpenAiCompatibleTranscriptionClient>.Instance);

        Result<AiTranscriptionResult, Error> result = await client.TranscribeAsync(
            CreateRequest(audioBytes: [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("ai.transcription.invalid", result.Error.Messages[0].Code);
    }

    [Fact]
    public void ResolveFullText_FallsBackToJoinedSegments_WhenAggregateTextEmpty()
    {
        // Регрессия #568: провайдер (AITunnel whisper-1) вернул сегменты, но ПУСТОЙ top-level "text".
        // Полный текст не должен теряться — иначе открытый ответ записывается пустым.
        AiTranscriptionSegment[] segments =
        [
            new(0, 1.5, "Запись — ссылочный тип"),
            new(1.5, 3, "со value-равенством."),
        ];
        const string expected = "Запись — ссылочный тип со value-равенством.";

        Assert.Equal(expected, OpenAiCompatibleTranscriptionClient.ResolveFullText("", segments));
        Assert.Equal(expected, OpenAiCompatibleTranscriptionClient.ResolveFullText(null, segments));
        Assert.Equal(expected, OpenAiCompatibleTranscriptionClient.ResolveFullText("   ", segments));
    }

    [Fact]
    public void ResolveFullText_PrefersAggregateText_WhenPresent()
    {
        AiTranscriptionSegment[] segments = [new(0, 1, "сегмент")];
        Assert.Equal(
            "Полный текст ответа",
            OpenAiCompatibleTranscriptionClient.ResolveFullText("  Полный текст ответа  ", segments));
    }

    [Fact]
    public void ResolveFullText_ReturnsEmpty_WhenNoTextAndNoSegments()
    {
        Assert.Equal(string.Empty, OpenAiCompatibleTranscriptionClient.ResolveFullText(null, []));
    }

    [Fact]
    public void ResolveDurationSeconds_uses_top_level_duration_without_segments()
    {
        Result<double, Error> result =
            OpenAiCompatibleTranscriptionClient.ResolveDurationSeconds(201.5, []);

        Assert.True(result.IsSuccess);
        Assert.Equal(201.5, result.Value);
    }

    [Fact]
    public void ResolveDurationSeconds_uses_the_larger_authoritative_value()
    {
        Result<double, Error> result = OpenAiCompatibleTranscriptionClient.ResolveDurationSeconds(
            10,
            [new AiTranscriptionSegment(0, 12, "segment")]);

        Assert.True(result.IsSuccess);
        Assert.Equal(12, result.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ResolveDurationSeconds_rejects_invalid_top_level_duration(double duration)
    {
        Result<double, Error> result =
            OpenAiCompatibleTranscriptionClient.ResolveDurationSeconds(duration, []);

        Assert.True(result.IsFailure);
        Assert.Equal("ai.transcription.invalid", result.Error.Messages[0].Code);
    }

    [Fact]
    public void ResolveDurationSeconds_returns_zero_when_duration_is_unavailable()
    {
        Result<double, Error> result =
            OpenAiCompatibleTranscriptionClient.ResolveDurationSeconds(null, []);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    private static AiTranscriptionRequest CreateRequest(
        IReadOnlyList<byte> audioBytes,
        string model = "openai/gpt-4o-mini-transcribe",
        string fileName = "audio.mp3",
        string contentType = "audio/mpeg",
        string? languageHint = null,
        string? prompt = null,
        int? timeoutSeconds = 30) =>
        new(model, fileName, contentType, audioBytes, languageHint, prompt, timeoutSeconds);

    private static ServiceProvider CreateServiceProvider(
        string apiKey = "test-api-key",
        string baseUrl = "https://api.example.test/v1/")
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configValues = new Dictionary<string, string?>
        {
            [$"{AiOptions.SECTION_NAME}:ApiKey"] = apiKey,
            [$"{AiOptions.SECTION_NAME}:Kind"] = "OpenAiCompatible",
            [$"{AiOptions.SECTION_NAME}:BaseUrl"] = baseUrl,
            [$"{AiOptions.SECTION_NAME}:TimeoutSeconds"] = "30",
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        services.AddAi(
            configuration.GetSection(AiOptions.SECTION_NAME),
            static providers => providers.AddOpenAiCompatible());

        return services.BuildServiceProvider();
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
