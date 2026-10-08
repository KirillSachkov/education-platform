using System.Data.Common;
using System.Threading.RateLimiting;
using AccessService.Core.Database;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Integrations.GitHubApp.Services;
using AccessService.Infrastructure.Postgres;
using AuthService.Contracts.HttpCommunication;
using ContentAccess;
using ContentAccess.TestSupport;
using Core.Database;
using EducationContentService.Contracts.HttpCommunication;
using TelegramBotService.Contracts.HttpCommunication;
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
using Respawn;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Testing;

namespace AccessService.IntegrationTests.Infrastructure;

/// <summary>
/// Postgres testcontainer + Respawn + Wolverine outbox storage. RabbitMQ external
/// transport is disabled via <c>DisableAllExternalWolverineTransports</c>; the rabbitmq
/// health check is removed because the test factory doesn't connect to a real broker.
/// </summary>
public class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("access_db_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection? _dbConnection;

    public FakeUserGrantWriter UserGrants { get; } = new();

    public FakeAuthServiceClient AuthClient { get; } = new();

    public FakeEducationContentServiceClient EduClient { get; } = new();

    public FakeTelegramBotServiceClient TelegramClient { get; } = new();

    public FakeTBankClient TBankClient { get; } = new();

    /// <summary>Управляемый GitHub App API (членство в org, приглашения) — #501.</summary>
    public FakeGitHubAppApiClient GitHubApi { get; } = new();

    /// <summary>
    /// Controllable entitlement checker for <c>GET /access/me/home-pins/</c> per-item lock
    /// state (epic #397). Real Redis-backed checker is skipped in Testing (see Program.cs),
    /// so without this registration the home-pins handler can't be resolved.
    /// </summary>
    public FakeEntitlementChecker EntitlementChecker { get; } = new();

    public TestOutboxCollector OutboxCollector { get; } = new();

    private string ConnectionString => _dbContainer.GetConnectionString() + ";Search Path=access,public";

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        // Mirror env var so anything reading via configuration before WebHost build sees it.
        // RabbitMq is a syntactically valid AMQP URI so RabbitMqHealthCheck can construct a
        // Uri at registration time; the actual transport is disabled via
        // DisableAllExternalWolverineTransports below.
        Environment.SetEnvironmentVariable("ConnectionStrings__Database", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", "amqp://localhost:5672");

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AccessServiceDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<AccessServiceDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
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

        // TBankOptions has [Required] on TerminalKey + Password and ValidateOnStart()
        // — provide dummy values so the host starts. Real T-Bank calls are intercepted
        // by FakeTBankClient which is registered as ITBankClient further down.
        builder.UseSetting("TBank:TerminalKey", "test-terminal");
        builder.UseSetting("TBank:Password", "test-password");

        // Billing default = enabled (mirrors dev/docker). The runtime admin-toggle
        // (issue #412/#414) reads `billing_config` row first; tests that exercise the
        // "billing disabled" path seed that row explicitly with IsEnabled=false.
        builder.UseSetting("Billing:DefaultEnabled", "true");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
                ["ConnectionStrings:RabbitMq"] = "amqp://localhost:5672",
                ["Authentication:Authority"] = null,
                ["Authentication:RequireHttpsMetadata"] = "false",
                ["DevAuth:Disabled"] = "true",
                ["TBank:TerminalKey"] = "test-terminal",
                ["TBank:Password"] = "test-password",
                ["Billing:DefaultEnabled"] = "true",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AccessServiceDbContext>();
            services.AddDbContextPool<AccessServiceDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
            });

            // Wolverine: stub external transports AND envelope persistence; outbox-flush
            // assertions делаются через TestOutboxCollector вместо Wolverine TrackActivity API.
            // Без DisableAllWolverineMessagePersistence durability agent может ловить 40P01
            // deadlock с Respawn.ResetAsync (см. issue #234).
            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();

            services.RemoveAll<ITransactionManager>();
            services.AddScoped<ITransactionManager, TestTransactionManager<AccessServiceDbContext>>();
            services.RemoveAll<IOutboxService>();
            services.AddSingleton(OutboxCollector);
            services.AddScoped<IOutboxService, TestOutboxService>();

            // ContentAccess: register fake user-grant writer (Phase 4). Real Redis is
            // skipped because Program.cs short-circuits the registration when env=Testing.
            services.AddSingleton<IUserGrantWriter>(UserGrants);

            // IEntitlementChecker is normally provided by AddContentAccessRedis, which
            // Program.cs skips in Testing. The home-pins handler (#397) depends on it for
            // per-item lock state, so wire the controllable fake here.
            services.RemoveAll<IEntitlementChecker>();
            services.AddSingleton<IEntitlementChecker>(EntitlementChecker);

            // AuthService HTTP client: replace the typed HttpClient with an in-memory
            // fake so user-enrichment in ListPlanGrants and `/access/users/lookup`
            // doesn't try to connect to a real AuthService.
            services.RemoveAll<IAuthServiceClient>();
            services.AddSingleton<IAuthServiceClient>(AuthClient);

            // EducationContentService HTTP client: same pattern. Used by
            // GetPublicPlans / GetPlanBySlug for course-titles enrichment of
            // COURSES plans. Tests preload `EduClient.CourseTitlesById` if needed.
            services.RemoveAll<IEducationContentServiceClient>();
            services.AddSingleton<IEducationContentServiceClient>(EduClient);

            // TelegramBotService HTTP client: dispatched by SetOnboardingEnabled handler
            // to ensure TELEGRAM step on enable when plan already has chat-bindings.
            services.RemoveAll<ITelegramBotServiceClient>();
            services.AddSingleton<ITelegramBotServiceClient>(TelegramClient);

            // T-Bank HTTP client: replace the typed HttpClient registration with the
            // in-memory fake (singleton, so tests can configure InitHandler/GetStateHandler
            // via Factory.TBankClient before invoking endpoints).
            services.RemoveAll<ITBankClient>();
            services.AddSingleton<ITBankClient>(TBankClient);

            // GitHub App API: реальный клиент ходит в api.github.com через
            // installation-token — подменяем управляемым фейком (#501).
            services.RemoveAll<IGitHubAppApiClient>();
            services.AddSingleton<IGitHubAppApiClient>(GitHubApi);
            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                List<HealthCheckRegistration> toRemove =
                    options.Registrations.Where(x => x.Name == "rabbitmq").ToList();
                foreach (HealthCheckRegistration registration in toRemove)
                    options.Registrations.Remove(registration);
            });

            // Rate limit: replace prod policies (which throttle) with NoLimiter for tests.
            // RemoveAll IConfigureOptions сначала, иначе AddPolicy upalls с
            // "policy already exists". См. docs/agents/wolverine-tests.md.
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.Configure<RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = 429;
                string[] policies =
                [
                    "user-lookup",
                    "anonymous-read",
                    "github-app-install",
                    "github-app-invitation",
                    "github-webhook",
                    "upgrade-quote",
                    "order-create",
                    "tbank-webhook",
                    "invite-redeem",
                    "claim-free",
                ];
                foreach (string policy in policies)
                {
                    options.AddPolicy(policy, _ => RateLimitPartition.GetNoLimiter(""));
                }
            });

            // Replace JWT validation with TestJwtHelper signing key — no real OIDC discovery
            // happens against AuthService during tests.
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
        });
    }

    public async Task ResetDatabaseAsync()
    {
        if (_dbConnection is null)
            return;

        OutboxCollector.Clear();
        await _respawner.ResetAsync(_dbConnection);
    }

    private async Task InitializeRespawner()
    {
        _respawner = await Respawner.CreateAsync(_dbConnection!,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["access"],
            });
    }
}
