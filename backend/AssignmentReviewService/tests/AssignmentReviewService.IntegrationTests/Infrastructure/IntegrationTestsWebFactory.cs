using System.Data.Common;
using AssignmentReviewService.Core.AiSettings;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Vcs;
using AssignmentReviewService.Web;
using AuthService.Contracts.HttpCommunication;
using Shared.AI;
using Core.Database;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using SharedKernel.DomainEvents;
using Wolverine;
using Wolverine.Testing;

namespace AssignmentReviewService.IntegrationTests.Infrastructure;

/// <summary>
///     Postgres testcontainer + Respawn + Wolverine outbox storage. Используется
///     <c>pgvector/pgvector:pg16</c> image: RAG-pipeline выпилен (#320), но
///     историческая миграция <c>20260510112103_InitialCreate</c> всё ещё содержит
///     <c>ALTER TABLE ... ADD COLUMN embedding vector(1536)</c> и HNSW-индекс
///     (миграции immutable per <c>docs/agents/migrations.md</c>).
///     На свежем testcontainer'е EF Core прокатывает всю историю миграций по
///     порядку, поэтому <c>vector</c> тип обязан существовать к моменту
///     InitialCreate. <c>DropRagInfrastructure</c> снесёт колонку и индекс
///     позже в цепочке. RabbitMQ external transport заглушён через
///     <c>DisableAllExternalWolverineTransports</c>; rabbitmq health check
///     удалён (мы не подключаемся к реальному брокеру).
/// </summary>
public class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .WithDatabase("assignment_review_db_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection? _dbConnection;

    /// <summary>
    ///     Shared outbox collector — Pattern A. Tests assert published integration
    ///     events via <c>OutboxCollector.OfType&lt;T&gt;()</c> instead of Wolverine
    ///     <c>TrackActivity</c>. Cleared in <see cref="ResetDatabaseAsync"/> before
    ///     each test.
    /// </summary>
    public TestOutboxCollector OutboxCollector { get; } = new();

    /// <summary>Stub IVcsProvider — tests configure responses per-case via this fake.</summary>
    public FakeVcsProvider VcsProvider { get; } = new();

    /// <summary>Stub IAiClient — tests configure outputs per-case.</summary>
    public FakeAiClient AiClient { get; } = new();

    /// <summary>Stub IAuthServiceClient — webhook-recovery tests (#451) configure github-id → userId.</summary>
    public FakeAuthServiceClient AuthClient { get; } = new();

    /// <summary>Webhook secret для HMAC test'ов (configured via in-memory configuration).</summary>
    public const string TestWebhookSecret = "test-webhook-secret-32-bytes-long!";

    private string ConnectionString =>
        _dbContainer.GetConnectionString() + ";Search Path=assignment_review,public";

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        // Mirror env var so anything reading via configuration before WebHost build sees it.
        Environment.SetEnvironmentVariable("ConnectionStrings__Database", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", "amqp://localhost:5672");

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AssignmentReviewServiceDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<AssignmentReviewServiceDbContext>();

        // Historical migration 20260510112103_InitialCreate raw-SQL'ит
        // `ALTER TABLE ... ADD COLUMN embedding vector(1536)` и HNSW-индекс.
        // RAG выпилен (#320), но миграции immutable — extension должна быть
        // в БД ДО Migrate(), иначе InitialCreate падает с 42704 "type vector
        // does not exist" и весь suite валится в InitializeAsync.
        await dbContext.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");

        await dbContext.Database.MigrateAsync();
        await WolverineSchemaHelper.CreateTablesAsync(dbContext);

        _dbConnection = new NpgsqlConnection(ConnectionString);
        await _dbConnection.OpenAsync();
        await InitializeRespawner();
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
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Database", ConnectionString);
        builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://localhost:5672");
        builder.UseSetting("AI:Kind", "OpenAiCompatible");
        builder.UseSetting("AI:BaseUrl", "https://api.polza.ai/api/v1/");
        builder.UseSetting("AI:ApiKey", "test-fake-api-key");
        builder.UseSetting("AI:TimeoutSeconds", "60");
        // #355: prod-дефолт ReviewEnabled=false. Тесты, кроме явных «disabled»-кейсов,
        // гоняют пайплайн с включённым тумблером — иначе gate скипнул бы создание AiReview.
        builder.UseSetting("AssignmentReviewAI:ReviewEnabled", "true");
        // #405: ретраи LLM-вызова мгновенные в тестах — не спим 10s+20s на каждом FAILED-кейсе.
        builder.UseSetting("AssignmentReviewAI:Limits:LlmRetryDelaySeconds", "0");
        builder.UseSetting("AssignmentReview:GitHub:Slug", "ars-test-app");
        builder.UseSetting("AssignmentReview:GitHub:AppId", "12345");
        builder.UseSetting("AssignmentReview:GitHub:WebhookSecret", TestWebhookSecret);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
                ["ConnectionStrings:RabbitMq"] = "amqp://localhost:5672",
                ["Authentication:Authority"] = null,
                ["Authentication:RequireHttpsMetadata"] = "false",
                ["DevAuth:Disabled"] = "true",
                // Phase 4: install/webhook tests требуют валидный Slug + WebhookSecret.
                ["AssignmentReview:GitHub:Slug"] = "ars-test-app",
                ["AssignmentReview:GitHub:AppId"] = "12345",
                ["AssignmentReview:GitHub:WebhookSecret"] = TestWebhookSecret,
                // AddOpenAiCompatible требует Kind/BaseUrl/ApiKey (ValidateOnStart).
                // Реальный HTTP в Polza не идёт — AiClient подменён на fake.
                ["AI:Kind"] = "OpenAiCompatible",
                ["AI:BaseUrl"] = "https://api.polza.ai/api/v1/",
                ["AI:ApiKey"] = "test-fake-api-key",
                ["AI:TimeoutSeconds"] = "60",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AssignmentReviewServiceDbContext>();
            services.AddDbContextPool<AssignmentReviewServiceDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
            });

            services.DisableAllExternalWolverineTransports();

            // Pattern A: disable Wolverine durability agent so it never polls the
            // wolverine_outgoing_envelopes / incoming_envelopes tables.  Without this,
            // the agent races with Respawn.ResetAsync and produces 40P01 deadlocks in CI.
            // Outbox assertions go through TestOutboxCollector instead of TrackActivity.
            services.DisableAllWolverineMessagePersistence();

            services.RemoveAll<ITransactionManager>();
            services.AddScoped<ITransactionManager>(sp =>
                new TestTransactionManager<AssignmentReviewServiceDbContext>(
                    sp.GetRequiredService<AssignmentReviewServiceDbContext>(),
                    sp.GetService<IDomainEventDispatcher>()));

            services.RemoveAll<IOutboxService>();
            services.AddSingleton(OutboxCollector);
            services.AddScoped<IOutboxService, TestOutboxService>();

            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                List<HealthCheckRegistration> toRemove =
                    options.Registrations.Where(x => x.Name == "rabbitmq").ToList();
                foreach (HealthCheckRegistration registration in toRemove)
                    options.Registrations.Remove(registration);
            });

            // Replace JWT validation — no real OIDC discovery в тестах.
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

            // Replace IVcsProvider with fake — avoids real GitHub HTTP в тестах.
            services.RemoveAll<IVcsProvider>();
            services.AddSingleton<IVcsProvider>(VcsProvider);

            // Replace IAuthServiceClient with fake — webhook-recovery (#451) резолвит юзера
            // установки через AuthService; в тестах подменяем без реального HTTP / service-токена.
            services.RemoveAll<IAuthServiceClient>();
            services.AddSingleton<IAuthServiceClient>(AuthClient);

            // Replace IAiClient with fake — RunIteration tests configure responses
            // per-case. MeteredAi* decorators ходят в живой Polza endpoint;
            // в тестах подменяем сам underlying client.
            services.RemoveAll<IAiClient>();
            services.AddSingleton<IAiClient>(AiClient);

            // Rate-limit policies — replace with NoLimiter so test suite не бьётся
            // в лимит при batch-runner'е. Каждая policy которую endpoint .RequireRateLimiting'ит
            // должна быть в этом списке — иначе ASP.NET Core возвращает 500 (не 429).
            services.RemoveAll<Microsoft.Extensions.Options.IConfigureOptions<
                Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>();
            services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = 429;
                string[] policies =
                    ["ar-github-install-start", "ar-github-webhook", "ar-review-iteration", "ar-student-reply"];
                foreach (string policy in policies)
                    options.AddPolicy(policy, _ =>
                        System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter(""));
            });
        });
    }

    public async Task ResetDatabaseAsync()
    {
        OutboxCollector.Clear();
        VcsProvider.Reset();
        AuthClient.Reset();

        // #355: resolver кэширует ai_model_settings в IMemoryCache (singleton на fixture).
        // Без сброса cached row (напр. ReviewEnabled=false из disabled-кейса) протёк бы в
        // следующий тест с уже сброшенной БД → ложный gate. Чистим перед каждым тестом.
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            scope.ServiceProvider
                .GetRequiredService<IAssignmentReviewAiModelSettingsResolver>()
                .InvalidateCache();
        }

        if (_dbConnection is null)
            return;

        await _respawner.ResetAsync(_dbConnection);
    }

    private async Task InitializeRespawner()
    {
        _respawner = await Respawner.CreateAsync(_dbConnection!,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["assignment_review"],
            });
    }
}
