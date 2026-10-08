using CSharpFunctionalExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shared.AI;
using Shared.AI.OpenAiCompatible;
using SharedKernel;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AI.OpenAiCompatible.Tests;

public sealed class OpenAiCompatiblePipelineTests
{
    [Fact]
    public async Task AnalyzeAsync_ReturnsModelRequired_WhenModelIsMissing()
    {
        using ServiceProvider serviceProvider = CreateServiceProvider();
        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = string.Empty,
            SystemPrompt = "System prompt",
            UserPrompt = "User prompt",
        };

        Result<AiBudgetAnalysis, Error> result = await aiClient.AnalyzeAsync(request, CancellationToken.None);

        AssertError(result.Error, "ai.model.required");
    }

    [Fact]
    public async Task GenerateAsync_ReturnsInputUnsupported_WhenAudioModelDoesNotSupportAudio()
    {
        using ServiceProvider serviceProvider = CreateServiceProvider(
            modelInfo: CreateModelInfo("text-model", supportsAudioInput: false));

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "text-model",
            SystemPrompt = "Transcribe audio",
            InputParts =
            [
                AiInputPart.AudioPart("audio", "sample.mp3", "audio/mpeg", [1, 2, 3]),
            ],
        };

        Result<AiGenerationResult<string>, Error> result = await aiClient.GenerateAsync<string>(
            request,
            CancellationToken.None);

        AssertError(result.Error, "ai.input.unsupported");
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsAnalysis_WhenRequestDoesNotFitContext()
    {
        using ServiceProvider serviceProvider = CreateServiceProvider(
            modelInfo: CreateModelInfo("small-model", contextWindowTokens: 100),
            estimatedInputTokens: 80);

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "small-model",
            SystemPrompt = "System prompt",
            UserPrompt = "User prompt",
            MaxOutputTokens = 30,
        };

        Result<AiBudgetAnalysis, Error> result = await aiClient.AnalyzeAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Fits);
        Assert.Equal(10, result.Value.OverflowTokens);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsContextExceeded_WhenRequestDoesNotFitContext()
    {
        using ServiceProvider serviceProvider = CreateServiceProvider(
            modelInfo: CreateModelInfo("small-model", contextWindowTokens: 100),
            estimatedInputTokens: 80);

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "small-model",
            SystemPrompt = "System prompt",
            UserPrompt = "User prompt",
            MaxOutputTokens = 30,
        };

        Result<AiGenerationResult<string>, Error> result = await aiClient.GenerateAsync<string>(
            request,
            CancellationToken.None);

        AssertError(result.Error, "ai.context.exceeded");
    }

    [Fact]
    public async Task GenerateAsync_ReturnsSchemaRequired_WhenJsonSchemaModeHasNoSchema()
    {
        using ServiceProvider serviceProvider = CreateServiceProvider(
            modelInfo: CreateModelInfo("schema-model", supportsJsonSchema: true));

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "schema-model",
            SystemPrompt = "System prompt",
            UserPrompt = "User prompt",
            MaxOutputTokens = 100,
            OutputMode = AiOutputMode.JsonSchema,
        };

        Result<AiGenerationResult<string>, Error> result = await aiClient.GenerateAsync<string>(
            request,
            CancellationToken.None);

        AssertError(result.Error, "ai.output.schema_required");
    }

    [Fact]
    public async Task GenerateAsync_ReturnsSchemaUnsupported_WhenModelSupportsNoJsonResponseFormat()
    {
        using ServiceProvider serviceProvider = CreateServiceProvider(
            modelInfo: CreateModelInfo(
                "schema-model",
                supportsJsonResponseFormat: false,
                supportsJsonSchema: false));

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "schema-model",
            SystemPrompt = "System prompt",
            UserPrompt = "User prompt",
            MaxOutputTokens = 100,
            OutputMode = AiOutputMode.JsonSchema,
            JsonSchema = new AiJsonSchema("payload", """{"type":"object"}"""),
        };

        Result<AiGenerationResult<string>, Error> result = await aiClient.GenerateAsync<string>(
            request,
            CancellationToken.None);

        AssertError(result.Error, "ai.output.schema_unsupported");
    }

    [Fact]
    public async Task GenerateAsync_ReturnsInputRequired_WhenOnlySystemPromptIsProvided()
    {
        using ServiceProvider serviceProvider = CreateServiceProvider(
            modelInfo: CreateModelInfo("model"));

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "model",
            SystemPrompt = "System prompt",
            MaxOutputTokens = 100,
        };

        Result<AiGenerationResult<string>, Error> result = await aiClient.GenerateAsync<string>(
            request,
            CancellationToken.None);

        AssertError(result.Error, "ai.input.required");
    }

    [Fact]
    public async Task GenerateAsync_ReturnsInputUnsupported_WhenAudioContentIsEmpty()
    {
        using ServiceProvider serviceProvider = CreateServiceProvider(
            modelInfo: CreateModelInfo("audio-model", supportsAudioInput: true));

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "audio-model",
            SystemPrompt = "Transcribe audio",
            MaxOutputTokens = 100,
            InputParts =
            [
                AiInputPart.AudioPart("audio", "sample.mp3", "audio/mpeg", []),
            ],
        };

        Result<AiGenerationResult<string>, Error> result = await aiClient.GenerateAsync<string>(
            request,
            CancellationToken.None);

        AssertError(result.Error, "ai.input.unsupported");
    }

    [Fact]
    public async Task GenerateAsync_ReturnsSuccess_WhenJsonSchemaResponseIsValid()
    {
        await using FakeOpenAiServer server = await FakeOpenAiServer.StartAsync(
            200,
            """
            {"id":"chatcmpl-test","object":"chat.completion","created":0,"model":"schema-model","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"{\"name\":\"ok\"}"}}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
            """);

        using ServiceProvider serviceProvider = CreateServiceProvider(
            modelInfo: CreateModelInfo("schema-model", supportsJsonSchema: true),
            baseUrl: server.BaseUrl);

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "schema-model",
            SystemPrompt = "System prompt",
            UserPrompt = "User prompt",
            MaxOutputTokens = 100,
            OutputMode = AiOutputMode.JsonSchema,
            JsonSchema = new AiJsonSchema(
                "payload",
                """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}"""),
        };

        Result<AiGenerationResult<SchemaPayload>, Error> result = await aiClient.GenerateAsync<SchemaPayload>(
            request,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value.Value.Name);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsOutputInvalid_WhenJsonSchemaResponseIsInvalid()
    {
        await using FakeOpenAiServer server = await FakeOpenAiServer.StartAsync(
            200,
            """
            {"id":"chatcmpl-test","object":"chat.completion","created":0,"model":"schema-model","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"{\"name\":123}"}}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
            """);

        using ServiceProvider serviceProvider = CreateServiceProvider(
            modelInfo: CreateModelInfo("schema-model", supportsJsonSchema: true),
            baseUrl: server.BaseUrl);

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "schema-model",
            SystemPrompt = "System prompt",
            UserPrompt = "User prompt",
            MaxOutputTokens = 100,
            OutputMode = AiOutputMode.JsonSchema,
            JsonSchema = new AiJsonSchema(
                "payload",
                """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}"""),
        };

        Result<AiGenerationResult<SchemaPayload>, Error> result = await aiClient.GenerateAsync<SchemaPayload>(
            request,
            CancellationToken.None);

        AssertError(result.Error, "ai.output.invalid");
    }

    [Fact]
    public async Task GenerateAsync_ReturnsProviderUnauthorized_WhenProviderReturnsUnauthorized()
    {
        await using FakeOpenAiServer server = await FakeOpenAiServer.StartAsync(
            401,
            """{"error":{"message":"invalid api key","type":"authentication_error"}}""");

        using ServiceProvider serviceProvider = CreateServiceProvider(
            modelInfo: CreateModelInfo("model"),
            baseUrl: server.BaseUrl);

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "model",
            SystemPrompt = "System prompt",
            UserPrompt = "User prompt",
            MaxOutputTokens = 100,
            OutputMode = AiOutputMode.Text,
        };

        Result<AiGenerationResult<string>, Error> result = await aiClient.GenerateAsync<string>(
            request,
            CancellationToken.None);

        AssertError(result.Error, "ai.provider.unauthorized");
    }

    [Fact]
    public async Task GenerateAsync_UsesModelsCatalogEndpoint_ForMetadataLookup()
    {
        await using FakeOpenAiCompatibleServer server = await FakeOpenAiCompatibleServer.StartAsync();

        using ServiceProvider serviceProvider = CreateServiceProvider(
            estimatedInputTokens: 10,
            baseUrl: server.BaseUrl,
            useRealModelCatalog: true);

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "audio-model",
            SystemPrompt = "Transcribe audio",
            UserPrompt = "Return text",
            MaxOutputTokens = 100,
            OutputMode = AiOutputMode.Text,
            InputParts =
            [
                AiInputPart.AudioPart("audio", "sample.mp3", "audio/mpeg", [1, 2, 3]),
            ],
        };

        Result<AiGenerationResult<string>, Error> result = await aiClient.GenerateAsync<string>(
            request,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value.Value);
        Assert.Contains("/api/v1/models", server.RequestPaths);
        Assert.DoesNotContain("/api/v1/models/audio-model", server.RequestPaths);
    }

    [Fact]
    public async Task GenerateAsync_Succeeds_WhenModelsCatalogContainsDuplicateModelIds()
    {
        await using FakeOpenAiCompatibleServer server = await FakeOpenAiCompatibleServer.StartAsync(
            """
            {"object":"list","data":[{"id":"audio-model","context_length":4096,"architecture":{"input_modalities":["audio"]},"supported_parameters":["response_format"]},{"id":"audio-model","context_length":8192,"architecture":{"input_modalities":["text","audio"]},"supported_parameters":["structured_outputs"]},{"id":"perplexity/pplx-embed-v1-4b","context_length":8192,"architecture":{"input_modalities":["text"]},"supported_parameters":[]},{"id":"perplexity/pplx-embed-v1-4b","context_length":8192,"architecture":{"input_modalities":["text"]},"supported_parameters":[]}]}
            """);

        using ServiceProvider serviceProvider = CreateServiceProvider(
            estimatedInputTokens: 10,
            baseUrl: server.BaseUrl,
            useRealModelCatalog: true);

        IAiClient aiClient = serviceProvider.GetRequiredService<IAiClientFactory>().Get(null);
        var request = new AiGenerationRequest
        {
            Model = "audio-model",
            SystemPrompt = "Transcribe audio",
            UserPrompt = "Return text",
            MaxOutputTokens = 100,
            OutputMode = AiOutputMode.Text,
            InputParts =
            [
                AiInputPart.AudioPart("audio", "sample.mp3", "audio/mpeg", [1, 2, 3]),
            ],
        };

        Result<AiGenerationResult<string>, Error> result = await aiClient.GenerateAsync<string>(
            request,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value.Value);
    }

    private static ServiceProvider CreateServiceProvider(
        AiModelInfo? modelInfo = null,
        int estimatedInputTokens = 10,
        string? baseUrl = null,
        bool useRealModelCatalog = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Test seam: pre-register IAiModelCatalog mock + IAiTokenEstimator mock _before_
        // вызова AddAi — multi-provider factory подбирает их через DI (TryAddSingleton
        // в OpenAiCompatible adapter не перезапишет уже зарегистрированный).
        if (!useRealModelCatalog)
        {
            IAiModelCatalog modelCatalog = Substitute.For<IAiModelCatalog>();
            if (modelInfo is not null)
            {
                modelCatalog
                    .GetModelAsync(modelInfo.Model, Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(Result.Success<AiModelInfo, Error>(modelInfo)));
            }
            services.AddSingleton(modelCatalog);
        }

        IAiTokenEstimator tokenEstimator = Substitute.For<IAiTokenEstimator>();
        tokenEstimator
            .Estimate(Arg.Any<AiTokenEstimateRequest>())
            .Returns(new AiTokenEstimate(estimatedInputTokens, AiTokenEstimateConfidence.Medium));
        services.AddSingleton(tokenEstimator);

        services.AddAi(
            BuildConfiguration(baseUrl).GetSection(AiOptions.SECTION_NAME),
            static providers => providers.AddRouterAi());

        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfiguration(string? baseUrl)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{AiOptions.SECTION_NAME}:ApiKey"] = "test-api-key",
            [$"{AiOptions.SECTION_NAME}:Kind"] = "RouterAI",
            [$"{AiOptions.SECTION_NAME}:BaseUrl"] = baseUrl ?? "https://routerai.test/api/v1/",
            [$"{AiOptions.SECTION_NAME}:TimeoutSeconds"] = "3",
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static AiModelInfo CreateModelInfo(
        string model,
        int contextWindowTokens = 8192,
        bool supportsAudioInput = true,
        bool supportsJsonResponseFormat = true,
        bool supportsJsonSchema = true,
        bool supportsSystemMessage = true) =>
        new(
            model,
            contextWindowTokens,
            supportsAudioInput,
            supportsJsonResponseFormat,
            supportsJsonSchema,
            supportsSystemMessage,
            IsMetadataAvailable: true);

    private static void AssertError(Error error, string expectedCode)
    {
        Assert.Contains(
            error.Messages,
            message => message.Code == expectedCode);
    }

    private static async Task<string> ReadRequestHeadersAsync(NetworkStream stream)
    {
        var buffer = new byte[1];
        var tail = new Queue<byte>(4);
        var headerBytes = new List<byte>();

        while (true)
        {
            int read = await stream.ReadAsync(buffer);
            if (read == 0)
                return Encoding.ASCII.GetString(headerBytes.ToArray());

            headerBytes.Add(buffer[0]);
            tail.Enqueue(buffer[0]);
            if (tail.Count > 4)
                tail.Dequeue();

            if (tail.Count == 4 && tail.SequenceEqual("\r\n\r\n"u8.ToArray()))
                return Encoding.ASCII.GetString(headerBytes.ToArray());
        }
    }

    private static int GetContentLength(string headersText)
    {
        foreach (string line in headersText.Split("\r\n"))
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                continue;

            return int.TryParse(
                line["Content-Length:".Length..].Trim(),
                out int contentLength)
                ? contentLength
                : 0;
        }

        return 0;
    }

    private static async Task ReadRequestBodyAsync(
        NetworkStream stream,
        int contentLength)
    {
        var buffer = new byte[Math.Min(contentLength, 8192)];
        int remaining = contentLength;

        while (remaining > 0)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)));
            if (read == 0)
                return;

            remaining -= read;
        }
    }

    private sealed class SchemaPayload
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class FakeOpenAiServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _requestTask;

        private FakeOpenAiServer(
            TcpListener listener,
            Task requestTask,
            string baseUrl)
        {
            _listener = listener;
            _requestTask = requestTask;
            BaseUrl = baseUrl;
        }

        public string BaseUrl { get; }

        public static async Task<FakeOpenAiServer> StartAsync(
            int statusCode,
            string responseBody)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task requestTask = HandleRequestAsync(listener, statusCode, responseBody);

            await Task.Yield();
            return new FakeOpenAiServer(listener, requestTask, $"http://127.0.0.1:{port}/api/v1/");
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();

            try
            {
                await _requestTask;
            }
            catch (ObjectDisposedException)
            {
                // Expected when listener is stopped during shutdown.
            }
            catch (SocketException)
            {
                // Expected when accept is interrupted by Stop().
            }
        }

        private static async Task HandleRequestAsync(
            TcpListener listener,
            int statusCode,
            string responseBody)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync();
            await using NetworkStream stream = client.GetStream();

            string headersText = await OpenAiCompatiblePipelineTests.ReadRequestHeadersAsync(stream);
            int contentLength = OpenAiCompatiblePipelineTests.GetContentLength(headersText);
            if (contentLength > 0)
                await OpenAiCompatiblePipelineTests.ReadRequestBodyAsync(stream, contentLength);

            byte[] body = Encoding.UTF8.GetBytes(responseBody);
            string statusText = statusCode == 200 ? "OK" : "Error";
            string headers =
                $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes);
            await stream.WriteAsync(body);
        }
    }

    private sealed class FakeOpenAiCompatibleServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task _requestTask;
        private string _modelsResponseBody = string.Empty;

        private FakeOpenAiCompatibleServer(
            TcpListener listener,
            Task requestTask,
            string baseUrl)
        {
            _listener = listener;
            _requestTask = requestTask;
            BaseUrl = baseUrl;
        }

        public string BaseUrl { get; }

        public List<string> RequestPaths { get; } = [];

        public static async Task<FakeOpenAiCompatibleServer> StartAsync(string? modelsResponseBody = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = new FakeOpenAiCompatibleServer(listener, Task.CompletedTask, $"http://127.0.0.1:{port}/api/v1/");
            server._modelsResponseBody = modelsResponseBody ??
                """
                {"object":"list","data":[{"id":"audio-model","context_length":8192,"architecture":{"input_modalities":["text","audio"]},"supported_parameters":["response_format","structured_outputs"]}]}
                """;
            server._requestTask = server.HandleRequestsAsync();

            await Task.Yield();
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _cancellationTokenSource.CancelAsync();
            _listener.Stop();
            _cancellationTokenSource.Dispose();

            try
            {
                await _requestTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation during shutdown.
            }
            catch (ObjectDisposedException)
            {
                // Expected when listener is disposed.
            }
            catch (SocketException)
            {
                // Expected when accept is interrupted by Stop().
            }
        }

        private async Task HandleRequestsAsync()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_cancellationTokenSource.Token);
                await using NetworkStream stream = client.GetStream();

                string headersText = await OpenAiCompatiblePipelineTests.ReadRequestHeadersAsync(stream);
                string requestLine = headersText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[0];
                string path = requestLine.Split(' ')[1];
                RequestPaths.Add(path);

                int contentLength = OpenAiCompatiblePipelineTests.GetContentLength(headersText);
                if (contentLength > 0)
                    await OpenAiCompatiblePipelineTests.ReadRequestBodyAsync(stream, contentLength);

                if (path.Equals("/api/v1/models", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(
                        stream,
                        200,
                        _modelsResponseBody);
                    continue;
                }

                if (path.Equals("/api/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(
                        stream,
                        200,
                        """
                        {"id":"chatcmpl-test","object":"chat.completion","created":0,"model":"audio-model","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
                        """);
                    continue;
                }

                await WriteResponseAsync(stream, 404, """{"error":{"message":"not found"}}""");
            }
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            int statusCode,
            string responseBody)
        {
            byte[] body = Encoding.UTF8.GetBytes(responseBody);
            string statusText = statusCode == 200 ? "OK" : "Error";
            string headers =
                $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes);
            await stream.WriteAsync(body);
        }
    }
}
