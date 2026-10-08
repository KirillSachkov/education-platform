using System.Data.Common;
using System.Globalization;
using System.Threading.RateLimiting;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Respawn;
using SearchService.Core.Database;
using SearchService.Core.Features.EducationDocuments.Queries;
using SearchService.Core.Reindex;
using SearchService.Infrastructure.Postgres;
using SearchService.Infrastructure.Typesense;
using SearchService.IntegrationTests.Mocks;
using StackExchange.Redis;
using TagService.Contracts.HttpCommunication;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Testcontainers.Typesense;
using SearchService.Web.Configuration;
using Typesense;
using Typesense.Setup;
using Wolverine;
using Wolverine.Testing;

namespace SearchService.IntegrationTests.Infrastructure;

public class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TypesenseApiKey = "test-typesense-api-key";

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("education_service_db_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:3-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7.2")
        .Build();

    private readonly TypesenseContainer _typesenseContainer = new TypesenseBuilder("typesense/typesense:30.1")
        .WithApiKey(TypesenseApiKey)
        .Build();

    private Respawner? _respawner;
    private DbConnection _dbConnection = null!;
    private IConnectionMultiplexer _redisConnection = null!;
    private bool _disposed;
    private readonly Dictionary<string, string?> _originalEnvironmentVariables = [];

    /// <summary>
    ///     Shared outbox collector — Pattern A. Tests assert published integration
    ///     events via <c>OutboxCollector.OfType&lt;T&gt;()</c> instead of Wolverine
    ///     <c>TrackActivity</c>. Cleared in <see cref="ResetDatabaseAsync"/> before
    ///     each test.
    /// </summary>
    public TestOutboxCollector OutboxCollector { get; } = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _dbContainer.StartAsync(),
            _rabbitMqContainer.StartAsync(),
            _redisContainer.StartAsync(),
            _typesenseContainer.StartAsync());

        string typesenseUrl = $"http://{_typesenseContainer.Hostname}:{_typesenseContainer.GetMappedPublicPort()}";
        SetEnvironmentVariable("ConnectionStrings__Database", _dbContainer.GetConnectionString());
        SetEnvironmentVariable("ConnectionStrings__RabbitMq", _rabbitMqContainer.GetConnectionString());
        SetEnvironmentVariable("ConnectionStrings__Redis", _redisContainer.GetConnectionString());
        SetEnvironmentVariable("TypesenseOptions__Url", typesenseUrl);
        SetEnvironmentVariable("TypesenseOptions__ApiKey", TypesenseApiKey);
        SetEnvironmentVariable("FileServiceOptions__Url", "http://file-service-test.local");
        SetEnvironmentVariable("FileServiceOptions__TimeoutSeconds", "10");

        _redisConnection = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());

        // Force the factory to create services now that the container is ready
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        SearchDbContext dbContext = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
        await WolverineSchemaHelper.CreateTablesAsync(dbContext);

        _dbConnection = new NpgsqlConnection(_dbContainer.GetConnectionString());
        await _dbConnection.OpenAsync();
        await InitializeRespawner();

        // The hosted initializer blocks startup until the alias and target collection
        // exist. Assert that contract explicitly so a future registration regression
        // fails fixture setup instead of being hidden by test-only self-healing.
        await EnsureEducationSearchAliasReadyAsync();
    }

    private async Task EnsureEducationSearchAliasReadyAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ITypesenseClient typesense = scope.ServiceProvider.GetRequiredService<ITypesenseClient>();

        CollectionAliasResponse alias = await typesense.RetrieveCollectionAlias(
            SearchService.Domain.CollectionNames.EDUCATION_SEARCH);
        await typesense.RetrieveCollection(alias.CollectionName);
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            GC.SuppressFinalize(this);
            return;
        }

        _disposed = true;
        try
        {
            await base.DisposeAsync();
        }
        catch (PostgresException ex) when (
            ex.SqlState == PostgresErrorCodes.AdminShutdown ||
            ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // WebApplicationFactory can still be stopping Wolverine while the
            // ephemeral PostgreSQL container is already being torn down or when
            // Wolverine message storage is disabled in the test profile.
        }

        if (_dbConnection != null!)
        {
            await _dbConnection.CloseAsync();
            await _dbConnection.DisposeAsync();
        }

        await Task.WhenAll(
            _dbContainer.StopAsync(),
            _rabbitMqContainer.StopAsync(),
            _redisContainer.StopAsync(),
            _typesenseContainer.StopAsync());

        await _dbContainer.DisposeAsync();
        await _rabbitMqContainer.DisposeAsync();
        await _redisConnection.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _typesenseContainer.DisposeAsync();

        RestoreEnvironmentVariables();
        GC.SuppressFinalize(this);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Tests.json"), optional: true);

            string typesenseUrl = $"http://{_typesenseContainer.Hostname}:{_typesenseContainer.GetMappedPublicPort()}";

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _dbContainer.GetConnectionString(),
                ["ConnectionStrings:RabbitMq"] = _rabbitMqContainer.GetConnectionString(),
                ["ConnectionStrings:Redis"] = _redisContainer.GetConnectionString(),
                ["TypesenseOptions:Url"] = typesenseUrl,
                ["TypesenseOptions:ApiKey"] = TypesenseApiKey,
                ["FileServiceOptions:Url"] = "http://file-service-test.local",
                ["FileServiceOptions:TimeoutSeconds"] = "10",
                ["DevAuth:Disabled"] = "true",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SCHEME_NAME;
                options.DefaultChallengeScheme = TestAuthHandler.SCHEME_NAME;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SCHEME_NAME,
                _ => { });

            services.RemoveAll<SearchDbContext>();

            services.AddDbContextPool<SearchDbContext>((sp, options) =>
            {
                options.UseNpgsql(_dbContainer.GetConnectionString());
            });

            services.DisableAllExternalWolverineTransports();

            // Pattern A: disable Wolverine durability agent so it never polls the
            // wolverine_outgoing_envelopes / incoming_envelopes tables.  Without this,
            // the agent races with Respawn.ResetAsync and produces 40P01 deadlocks in CI.
            // Outbox assertions go through TestOutboxCollector instead of TrackActivity.
            services.DisableAllWolverineMessagePersistence();

            services.RemoveAll<IOutboxService>();
            services.AddSingleton(OutboxCollector);
            services.AddScoped<IOutboxService, TestOutboxService>();

            services.RemoveAll<IEducationContentServiceClient>();
            services.AddSingleton(_ => EducationContentServiceClientMockExtensions.CreateMock());

            services.RemoveAll<ITagServiceClient>();
            services.AddSingleton<MockTagServiceClient>();
            services.AddSingleton<ITagServiceClient>(sp => sp.GetRequiredService<MockTagServiceClient>());

            services.RemoveAll<ISearchIndexingConsumerController>();
            services.AddSingleton<ISearchIndexingConsumerController, NoopSearchIndexingConsumerController>();

            // Auto-reindex background services must not run during tests:
            // SearchReindexOnStartupService fires after 5s and triggers a full Typesense
            // rebuild (blue/green alias swap) that wipes all documents seeded by other tests.
            // SearchReindexReconciliationService does the same on a timer.
            ServiceDescriptor? startupSvc = services.FirstOrDefault(
                s => s.ImplementationType == typeof(SearchReindexOnStartupService));
            if (startupSvc is not null)
            {
                services.Remove(startupSvc);
            }

            ServiceDescriptor? reconciliationSvc = services.FirstOrDefault(
                s => s.ImplementationType == typeof(SearchReindexReconciliationService));
            if (reconciliationSvc is not null)
            {
                services.Remove(reconciliationSvc);
            }

            services.RemoveAll<ITypesenseClient>();
            services.AddTypesenseClient(
                config =>
                {
                    config.ApiKey = TypesenseApiKey;
                    config.Nodes = [
                        new Node(
                            _typesenseContainer.Hostname,
                            _typesenseContainer.GetMappedPublicPort().ToString(CultureInfo.InvariantCulture),
                            "http")];
                },
                enableHttpCompression: true);

            // The /search endpoint is gated by RequireRateLimiting("search-public").
            // Without this override the real limiter (30 req / 10s anonymous) makes
            // dense test runs flake with 429. Replace every policy used by the suite
            // with a no-limit partition. Add new policy names here when new endpoints
            // gain rate limiting.
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.Configure<RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = 429;
                string[] policies = [GetDocumentsEndpoint.SEARCH_PUBLIC_RATE_LIMIT_POLICY];
                foreach (string policy in policies)
                {
                    options.AddPolicy(policy, _ => RateLimitPartition.GetNoLimiter(""));
                }
            });
        });
    }

    /// <summary>
    /// Drops every document in the Typesense collection backing the alias. Cheaper
    /// than recreating the collection and preserves the startup-initialized alias.
    /// Tests use unique GUIDs in titles so usually this is belt-and-suspenders, but
    /// `TagsDeletedHandler` tests seed 500+ documents that pollute aggregate counts
    /// in downstream tests if not cleared.
    /// </summary>
    public async Task ClearEducationSearchCollectionAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ITypesenseClient typesense = scope.ServiceProvider.GetRequiredService<ITypesenseClient>();

        try
        {
            // `id:!=` matches every document. batchSize is a Typesense pacing knob,
            // not a hard limit on rows removed.
            await typesense.DeleteDocuments(
                SearchService.Domain.CollectionNames.EDUCATION_SEARCH,
                "id:!=`__never__`",
                batchSize: 1000);
        }
        catch (TypesenseApiNotFoundException)
        {
            // Alias/collection missing — first test in suite hasn't bootstrapped yet.
        }
    }

    public async Task ResetDatabaseAsync()
    {
        OutboxCollector.Clear();

        if (_respawner is not null)
        {
            await _respawner.ResetAsync(_dbConnection);
        }

        await _redisConnection.GetDatabase().ExecuteAsync("FLUSHDB");
        await ClearEducationSearchCollectionAsync();
    }

    private async Task InitializeRespawner()
    {
        try
        {
            _respawner = await Respawner.CreateAsync(_dbConnection,
                new RespawnerOptions
                {
                    DbAdapter = DbAdapter.Postgres,
                    SchemasToInclude = ["search"],
                });
        }
        catch (InvalidOperationException)
        {
            _respawner = null;
        }
    }

    private void SetEnvironmentVariable(string key, string? value)
    {
        if (!_originalEnvironmentVariables.ContainsKey(key))
        {
            _originalEnvironmentVariables[key] = Environment.GetEnvironmentVariable(key);
        }

        Environment.SetEnvironmentVariable(key, value);
    }

    private void RestoreEnvironmentVariables()
    {
        foreach ((string key, string? value) in _originalEnvironmentVariables)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
