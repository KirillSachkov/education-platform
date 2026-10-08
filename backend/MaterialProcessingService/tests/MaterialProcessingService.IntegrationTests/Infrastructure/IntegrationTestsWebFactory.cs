using System.Data.Common;
using System.Threading.RateLimiting;
using Core.Database;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Materials;
using EducationContentService.Contracts.SearchLookup;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NSubstitute;
using Respawn;
using SharedKernel;
using Testcontainers.PostgreSql;
using MaterialProcessingService.Core.Database;
using MaterialProcessingService.Core.Features.ContentDrafts;
using MaterialProcessingService.Core.Features.Timecodes;
using MaterialProcessingService.Core.Media;
using MaterialProcessingService.Core.Transcripts;
using MaterialProcessingService.Infrastructure.Postgres;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace MaterialProcessingService.IntegrationTests.Infrastructure;

public sealed class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("material_processing_service_db_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection _dbConnection = null!;
    private readonly Dictionary<Guid, GetVideoProcessingSourceResponse> _processingSources = [];

    private string ConnectionString => _dbContainer.GetConnectionString() + ";Search Path=material_processing,public";

    public IFileServiceClient FileServiceClient { get; } = Substitute.For<IFileServiceClient>();
    public IEducationContentServiceClient EducationContentServiceClient { get; } = Substitute.For<IEducationContentServiceClient>();
    public IMediaProbe MediaProbe { get; } = Substitute.For<IMediaProbe>();
    public IAudioExtractor AudioExtractor { get; } = Substitute.For<IAudioExtractor>();
    public ISpeechToTextProvider SpeechToTextProvider { get; } = Substitute.For<ISpeechToTextProvider>();
    public ITimecodeGenerator TimecodeGenerator { get; } = Substitute.For<ITimecodeGenerator>();
    public IVideoContentGenerator VideoContentGenerator { get; } = Substitute.For<IVideoContentGenerator>();
    public TestOutboxCollector OutboxCollector { get; } = new();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Database", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", "amqp://localhost:5672");
        Environment.SetEnvironmentVariable("Authentication__Authority", null);
        Environment.SetEnvironmentVariable("Authentication__RequireHttpsMetadata", "false");
        Environment.SetEnvironmentVariable("DevAuth__Disabled", "true");
        // Multi-provider AI config (issue #146) — legacy AI:ApiKey/Kind/BaseUrl
        // form is gone after the per-service appsettings switched to providers map.
        Environment.SetEnvironmentVariable("AI__Default", "aitunnel");
        Environment.SetEnvironmentVariable("AI__Providers__aitunnel__Kind", "OpenAiCompatible");
        Environment.SetEnvironmentVariable("AI__Providers__aitunnel__BaseUrl", "https://routerai.test/api/v1/");
        Environment.SetEnvironmentVariable("AI__Providers__aitunnel__TimeoutSeconds", "30");
        Environment.SetEnvironmentVariable("AI__Providers__aitunnel__ApiKey", "test-api-key");
        Environment.SetEnvironmentVariable("VideoProcessingAI__SpeechToText__Model", "test-audio-model");
        Environment.SetEnvironmentVariable("VideoProcessingAI__SpeechToText__Temperature", "0");
        Environment.SetEnvironmentVariable("VideoProcessingAI__SpeechToText__MaxOutputTokens", "4000");
        Environment.SetEnvironmentVariable("VideoProcessingAI__SpeechToText__TimeoutSeconds", "300");
        Environment.SetEnvironmentVariable("VideoProcessingAI__TimecodeGeneration__Model", "test-chat-model");
        Environment.SetEnvironmentVariable("VideoProcessingAI__TimecodeGeneration__Temperature", "0.1");
        Environment.SetEnvironmentVariable("VideoProcessingAI__TimecodeGeneration__MaxOutputTokens", "6000");
        Environment.SetEnvironmentVariable("VideoProcessingAI__TimecodeGeneration__TimeoutSeconds", "300");
        Environment.SetEnvironmentVariable("VideoProcessingAI__ContentGeneration__Model", "test-chat-model");
        Environment.SetEnvironmentVariable("VideoProcessingAI__ContentGeneration__Temperature", "0.2");
        Environment.SetEnvironmentVariable("VideoProcessingAI__ContentGeneration__MaxOutputTokens", "8000");
        Environment.SetEnvironmentVariable("VideoProcessingAI__ContentGeneration__TimeoutSeconds", "300");
        Environment.SetEnvironmentVariable("EducationServiceOptions__Url", "http://education-content.test");
        Environment.SetEnvironmentVariable("EducationServiceOptions__TimeoutSeconds", "30");

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        MaterialProcessingServiceDbContext dbContext = scope.ServiceProvider.GetRequiredService<MaterialProcessingServiceDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
        await WolverineSchemaHelper.CreateTablesAsync(dbContext);

        _dbConnection = new NpgsqlConnection(ConnectionString);
        await _dbConnection.OpenAsync();
        await InitializeRespawner();
        ConfigureDefaultMocks();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        if (_dbConnection is not null)
        {
            await _dbConnection.CloseAsync();
            await _dbConnection.DisposeAsync();
        }

        await _dbContainer.StopAsync();
        await _dbContainer.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Database", ConnectionString);
        builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://localhost:5672");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Tests.json"), optional: true);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
                ["ConnectionStrings:RabbitMq"] = "amqp://localhost:5672",
                ["Authentication:Authority"] = null,
                ["Authentication:RequireHttpsMetadata"] = "false",
                ["DevAuth:Disabled"] = "true",
                ["AI:Default"] = "aitunnel",
                ["AI:Providers:aitunnel:Kind"] = "OpenAiCompatible",
                ["AI:Providers:aitunnel:BaseUrl"] = "https://routerai.test/api/v1/",
                ["AI:Providers:aitunnel:TimeoutSeconds"] = "30",
                ["AI:Providers:aitunnel:ApiKey"] = "test-api-key",
                ["VideoProcessingAI:SpeechToText:Provider"] = "aitunnel",
                ["VideoProcessingAI:SpeechToText:Model"] = "test-audio-model",
                ["VideoProcessingAI:SpeechToText:Temperature"] = "0",
                ["VideoProcessingAI:SpeechToText:MaxOutputTokens"] = "4000",
                ["VideoProcessingAI:SpeechToText:TimeoutSeconds"] = "300",
                ["VideoProcessingAI:TimecodeGeneration:Provider"] = "aitunnel",
                ["VideoProcessingAI:TimecodeGeneration:Model"] = "test-chat-model",
                ["VideoProcessingAI:TimecodeGeneration:Temperature"] = "0.1",
                ["VideoProcessingAI:TimecodeGeneration:MaxOutputTokens"] = "6000",
                ["VideoProcessingAI:TimecodeGeneration:TimeoutSeconds"] = "300",
                ["VideoProcessingAI:ContentGeneration:Provider"] = "aitunnel",
                ["VideoProcessingAI:ContentGeneration:Model"] = "test-chat-model",
                ["VideoProcessingAI:ContentGeneration:Temperature"] = "0.2",
                ["VideoProcessingAI:ContentGeneration:MaxOutputTokens"] = "8000",
                ["VideoProcessingAI:ContentGeneration:TimeoutSeconds"] = "300",
                ["EducationServiceOptions:Url"] = "http://education-content.test",
                ["EducationServiceOptions:TimeoutSeconds"] = "30",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<MaterialProcessingServiceDbContext>();
            services.AddDbContextPool<MaterialProcessingServiceDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
            });

            services.RemoveAll<IFileServiceClient>();
            services.AddSingleton(FileServiceClient);

            services.RemoveAll<IEducationContentServiceClient>();
            services.AddSingleton(EducationContentServiceClient);

            services.RemoveAll<IMediaProbe>();
            services.AddSingleton(MediaProbe);

            services.RemoveAll<IAudioExtractor>();
            services.AddSingleton(AudioExtractor);

            services.RemoveAll<ISpeechToTextProvider>();
            services.AddSingleton(SpeechToTextProvider);

            services.RemoveAll<ITimecodeGenerator>();
            services.AddSingleton(TimecodeGenerator);

            services.RemoveAll<IVideoContentGenerator>();
            services.AddSingleton(VideoContentGenerator);

            services.RemoveAll<ITransactionManager>();
            services.AddScoped<ITransactionManager, TestTransactionManager>();

            services.RemoveAll<IOutboxService>();
            services.AddSingleton(OutboxCollector);
            services.AddScoped<IOutboxService, TestOutboxService>();

            services.RemoveAll<IDbContextOutbox<MaterialProcessingServiceDbContext>>();

            // Заменить prod policy «ai-generation» (6 req/час) на NoLimiter:
            // тесты гоняют десятки enqueue одного и того же endpoint'а под одним
            // user-id, без NoLimiter все упадут с 429. Per docs/agents/wolverine-tests.md.
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.Configure<RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = 429;
                options.AddPolicy("ai-generation",
                    _ => RateLimitPartition.GetNoLimiter(""));
            });

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.MetadataAddress = null!;
                options.ConfigurationManager = null;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestJwtHelper.TEST_ISSUER,
                    ValidateAudience = true,
                    ValidAudience = TestJwtHelper.TEST_AUDIENCE,
                    ValidateLifetime = true,
                    IssuerSigningKey = TestJwtHelper.GetSigningKey(),
                    ValidateIssuerSigningKey = true,
                };
            });

            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();

            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                List<HealthCheckRegistration> toRemove = options.Registrations
                    .Where(x => x.Name == "rabbitmq")
                    .ToList();

                foreach (HealthCheckRegistration registration in toRemove)
                    options.Registrations.Remove(registration);
            });
        });
    }

    public async Task ResetDatabaseAsync()
    {
        _processingSources.Clear();
        ConfigureDefaultMocks();
        ClearMockCalls();
        OutboxCollector.Clear();
        await _respawner.ResetAsync(_dbConnection);

        // Singleton IMemoryCache переживает Respawn → resolver продолжает отдавать
        // settings от прошлого теста. Сбрасываем явно, чтобы Get-тест увидел
        // CONFIG-источник, а не stale DATABASE.
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        MaterialProcessingService.Core.AiSettings.IAiModelSettingsResolver resolver =
            scope.ServiceProvider.GetRequiredService<MaterialProcessingService.Core.AiSettings.IAiModelSettingsResolver>();
        resolver.Invalidate();
    }

    private void ConfigureDefaultMocks()
    {
        FileServiceClient.GetVideoProcessingSourceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid videoId = callInfo.ArgAt<Guid>(0);
                if (_processingSources.TryGetValue(videoId, out GetVideoProcessingSourceResponse? existingResponse))
                {
                    return Task.FromResult(Result.Success<GetVideoProcessingSourceResponse?, Error>(existingResponse));
                }

                GetVideoProcessingSourceResponse response = new(
                    videoId,
                    "READY",
                    Guid.CreateVersion7(),
                    120,
                    "HLS",
                    $"https://video.test/{videoId}/master.m3u8",
                    DateTime.UtcNow.AddMinutes(30));
                _processingSources[videoId] = response;

                return Task.FromResult(
                    Result.Success<GetVideoProcessingSourceResponse?, Error>(
                        response));
            });

        FileServiceClient.UpdateChaptersAsync(
                Arg.Any<Guid>(),
                Arg.Any<FileService.Contracts.Assets.UpdateVideoChaptersRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(UnitResult.Success<Error>()));

        EducationContentServiceClient.UpdateMaterialContentAsync(
                Arg.Any<Guid>(),
                Arg.Any<UpdateMaterialContentRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(UnitResult.Success<Error>()));

        // Default-lookup для GenerateVideoContent ForceOverwrite=false ветки —
        // материал существует, тело пустое, поэтому handler не падает на 409.
        // Тесты, где нужен «уже есть конспект», должны переопределить per-test.
        EducationContentServiceClient.GetMaterialSearchLookupAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid id = callInfo.ArgAt<Guid>(0);
                return Task.FromResult(
                    Result.Success<MaterialSearchLookupDto, Error>(
                        new MaterialSearchLookupDto(
                            Id: id,
                            CourseId: null,
                            CourseSlug: null,
                            Title: "test material",
                            ImageId: null,
                            ModuleId: null,
                            CourseTitle: null,
                            ModuleTitle: null,
                            Status: EducationContentService.Contracts.SearchLookup.PublicationStatus.DRAFT,
                            RequiredAccessTags: Array.Empty<string>(),
                            UpdatedAt: DateTime.UtcNow,
                            Content: null)));
            });

        MediaProbe.ProbeAsync(Arg.Any<VideoProcessingSource>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<MediaProbeResult, Error>(
                new MediaProbeResult(true, TimeSpan.FromMinutes(2)))));

        AudioExtractor.ExtractChunksAsync(Arg.Any<VideoProcessingSource>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<AudioChunk>, Error>(
                new List<AudioChunk>
                {
                    new("chunk-1.wav", TimeSpan.Zero, TimeSpan.FromMinutes(2))
                })));

        AudioExtractor.CleanupAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Failsafe для STT truncation defense (issue #110): если тест случайно
        // триггернёт recursion (last.End < 70% chunk duration), но забыл переопределить
        // SplitChunkAsync — упадёт с понятным error code, а не с NRE.
        AudioExtractor.SplitChunkAsync(Arg.Any<AudioChunk>(), Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<IReadOnlyList<AudioChunk>, Error>(
                Error.Failure(
                    "test.split.not_configured",
                    "Test triggered STT truncation defense but did not stub SplitChunkAsync"))));

        SpeechToTextProvider.TranscribeAsync(Arg.Any<AudioChunk>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<SpeechToTextResult, Error>(
                new SpeechToTextResult(
                    "ru",
                    [
                        new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(15), "Введение в тему"),
                        new TranscriptSegment(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45), "Разбор ключевых моментов"),
                        new TranscriptSegment(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(90), "Практические выводы")
                    ],
                    SpeechTimestampSource.Model))));

        TimecodeGenerator.GenerateAsync(Arg.Any<Transcript>(), Arg.Any<TimeSpan>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<GeneratedTimecodesResult, Error>(
                new GeneratedTimecodesResult(
                    "ru",
                    [
                        new GeneratedVideoTimecode(0, 30, "Введение", 0.95),
                        new GeneratedVideoTimecode(55, 95, "Основная часть", 0.91)
                    ]))));

        VideoContentGenerator.GenerateAsync(Arg.Any<Transcript>(), Arg.Any<TimeSpan>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<GeneratedVideoContentResult, Error>(
                new GeneratedVideoContentResult(
                    "ru",
                    "# Конспект\n\n- Первая мысль\n- Вторая мысль"))));
    }

    private void ClearMockCalls()
    {
        FileServiceClient.ClearReceivedCalls();
        EducationContentServiceClient.ClearReceivedCalls();
        MediaProbe.ClearReceivedCalls();
        AudioExtractor.ClearReceivedCalls();
        SpeechToTextProvider.ClearReceivedCalls();
        TimecodeGenerator.ClearReceivedCalls();
        VideoContentGenerator.ClearReceivedCalls();
    }

    private async Task InitializeRespawner()
    {
        _respawner = await Respawner.CreateAsync(
            _dbConnection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["material_processing"],
            });
    }
}
