using System.Data.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using AuthService.Core.Services;
using AuthService.Infrastructure.Postgres;
using AuthService.Web.Configuration;
using Core.Database;
using Respawn;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Testing;

namespace AuthService.IntegrationTests.Infrastructure;

public class IntegrationTestsWebFactory : WebApplicationFactory<Web.Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("auth_service_db_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection _dbConnection = null!;

    private string ConnectionString => _dbContainer.GetConnectionString() + ";Search Path=auth,public";

    public TestOutboxCollector OutboxCollector { get; } = new();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Database",
            ConnectionString);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AuthDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
        await WolverineSchemaHelper.CreateTablesAsync(dbContext);

        _dbConnection = new NpgsqlConnection(ConnectionString);
        await _dbConnection.OpenAsync();
        await InitializeRespawnerAsync();
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

        // Register the real OpenIddict GitHub client (scheme "GitHub") so the surviving
        // link/sync CHALLENGE branches are testable — the challenge builds the authorize
        // URL offline, no network involved. Callback tests still use TestGitHubOAuthHandler
        // via ForwardDefaultSelector.
        builder.UseSetting("GitHub:ClientId", "test-github-client-id");
        builder.UseSetting("GitHub:ClientSecret", "test-github-client-secret");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
                ["ConnectionStrings:RabbitMq"] = "amqp://localhost:5672",
                ["OpenIddict:EducationPlatform:ClientId"] = "test-client",
                ["OpenIddict:EducationPlatform:Secret"] = "test-secret",
                ["OpenIddict:EducationPlatform:DisplayName"] = "Test Client",
                ["OpenIddict:EducationPlatform:RedirectUri"] = "http://localhost/callback",
                ["OpenIddict:ServiceToService:ClientId"] = "test-s2s",
                ["OpenIddict:ServiceToService:Secret"] = "test-s2s-secret",
                ["OpenIddict:ServiceToService:DisplayName"] = "Test S2S",
                ["OpenIddict:AdminApi:ClientId"] = "test-mcp-admin",
                ["OpenIddict:AdminApi:Secret"] = "test-mcp-admin-secret",
                ["OpenIddict:AdminApi:DisplayName"] = "Test MCP Admin",
                ["Email:Host"] = "localhost",
                ["Email:From"] = "test@test.com",
                ["AuthService:FrontendBaseUrl"] = "http://localhost:3000",
                ["TelegramLinkOptions:BotUsername"] = "test_education_bot",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Production wiring persists DataProtection keys to Redis (`PersistKeysToStackExchangeRedis`);
            // in tests there's no Redis, so Identity cookie sign-in throws CryptographicException
            // when the cookie handler tries to encrypt the auth ticket. Swap in an in-memory provider.
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());

            services.RemoveAll<AuthDbContext>();
            services.AddDbContextPool<AuthDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString)
                    .UseOpenIddict<Guid>();
            });

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Some endpoints (e.g. UnlinkGitHub, GitHubLink) specify explicit auth schemes
            // (OpenIddict validation + Identity cookies) via policy.AddAuthenticationSchemes().
            // Forward those schemes to TestAuth when the TestAuth header is present, otherwise
            // fall through to the real handler (needed for OIDC flow tests that use cookies).
            static string? ForwardToTestAuthWhenHeaderPresent(HttpContext ctx)
            {
                string auth = ctx.Request.Headers.Authorization.ToString();
                return !string.IsNullOrEmpty(auth) &&
                       auth.StartsWith($"{TestAuthHandler.SchemeName} ", StringComparison.OrdinalIgnoreCase)
                    ? TestAuthHandler.SchemeName
                    : null;
            }

            services.PostConfigure<OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreOptions>(
                OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
                options => options.ForwardDefaultSelector = ForwardToTestAuthWhenHeaderPresent);

            services.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
                Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme,
                options => options.ForwardDefaultSelector = ForwardToTestAuthWhenHeaderPresent);

            // GitHub OAuth callback tests: when the X-Test-GitHub-Flow header is present,
            // forward the OpenIddict *client* scheme to a stub simulating a completed GitHub
            // handshake (provider key + github_flow item). Without the header the real
            // OpenIddict client handler stays in charge.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestGitHubOAuthHandler>(
                    TestGitHubOAuthHandler.SchemeName, _ => { });

            services.PostConfigure<OpenIddict.Client.AspNetCore.OpenIddictClientAspNetCoreOptions>(
                OpenIddict.Client.AspNetCore.OpenIddictClientAspNetCoreDefaults.AuthenticationScheme,
                options => options.ForwardDefaultSelector = ctx =>
                    ctx.Request.Headers.ContainsKey(TestGitHubOAuthHandler.FlowHeader)
                        ? TestGitHubOAuthHandler.SchemeName
                        : null);

            // Remove OpenIddictSeeder — it runs before migrations are applied in tests
            var seederDescriptors = services
                .Where(d => d.ImplementationType == typeof(OpenIddictSeeder))
                .ToList();
            foreach (var d in seederDescriptors)
                services.Remove(d);

            services.RemoveAll<IAuthEmailSender>();
            services.AddSingleton<FakeEmailSender>();
            services.AddSingleton<IAuthEmailSender>(sp => sp.GetRequiredService<FakeEmailSender>());

            services.RemoveAll<IOtpStore>();
            services.AddSingleton<FakeOtpStore>();
            services.AddSingleton<IOtpStore>(sp => sp.GetRequiredService<FakeOtpStore>());

            services.RemoveAll<ITelegramLinkTokenStore>();
            services.AddSingleton<FakeTelegramLinkTokenStore>();
            services.AddSingleton<ITelegramLinkTokenStore>(sp => sp.GetRequiredService<FakeTelegramLinkTokenStore>());

            services.RemoveAll<OtpAttemptLimiter>();
            services.AddSingleton<OtpAttemptLimiter>(new FakeOtpAttemptLimiter());

            // Disable Wolverine external transports (RabbitMQ) and durable persistence
            // so tests don't need RabbitMQ running
            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();
            services.RemoveAll<IDbContextOutbox<AuthDbContext>>();
            services.RemoveAll<ITransactionManager>();
            services.AddScoped<ITransactionManager, TestTransactionManager>();
            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                List<HealthCheckRegistration> toRemove =
                    options.Registrations.Where(x => x.Name is "smtp" or "rabbitmq" or "redis").ToList();

                foreach (HealthCheckRegistration registration in toRemove)
                {
                    options.Registrations.Remove(registration);
                }
            });
            services.RemoveAll<AuthService.Core.Database.IOutboxService>();
            services.AddSingleton(OutboxCollector);
            services.AddScoped<TestOutboxBuffer>();
            services.AddScoped<AuthService.Core.Database.IOutboxService, TestOutboxService>();
            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                List<HealthCheckRegistration> toRemove =
                    options.Registrations.Where(x => x.Name is "smtp" or "rabbitmq" or "redis").ToList();

                foreach (HealthCheckRegistration registration in toRemove)
                {
                    options.Registrations.Remove(registration);
                }
            });

            // Disable HTTPS requirement for OpenIddict in test environment
            services.Configure<OpenIddict.Server.AspNetCore.OpenIddictServerAspNetCoreOptions>(options =>
                options.DisableTransportSecurityRequirement = true);
            services.Configure<OpenIddict.Client.AspNetCore.OpenIddictClientAspNetCoreOptions>(options =>
                options.DisableTransportSecurityRequirement = true);

            // Allow Identity cookies over HTTP in tests (production forces Secure=Always)
            services.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
                Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme,
                options => options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest);

            // Disable rate limiting — replace the delegate that registers policies
            // with one that uses NoLimiter partitions instead of fixed windows
            services.RemoveAll<Microsoft.Extensions.Options.IConfigureOptions<
                Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>();
            services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = 429;
                string[] policies =
                    ["otp", "register", "login", "github", "password-reset", "anonymous-read", "token", "telegram-link"];
                foreach (string policy in policies)
                {
                    options.AddPolicy(policy, _ =>
                        System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter(string.Empty));
                }
            });
        });
    }

    public async Task ResetDatabaseAsync()
    {
        OutboxCollector.Clear();
        await _respawner.ResetAsync(_dbConnection);
    }

    private async Task InitializeRespawnerAsync()
    {
        _respawner = await Respawner.CreateAsync(_dbConnection,
            new RespawnerOptions { DbAdapter = DbAdapter.Postgres, SchemasToInclude = ["auth"], });
    }
}
