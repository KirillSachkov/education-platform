using System.Data.Common;
using System.Security.Claims;
using System.Threading.RateLimiting;
using AccessService.Contracts.HttpCommunication;
using AuthService.Contracts.HttpCommunication;
using Core.Database;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NotificationService.Core;
using NotificationService.Core.Channels.WebPush;
using NotificationService.Core.Database;
using NotificationService.Domain;
using NotificationService.Infrastructure.Postgres;
using NotificationService.Infrastructure.Postgres.Configurations;
using NotificationService.Web.Configuration;
using Npgsql;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Respawn;
using Shared.Email;
using SharedKernel;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Testing;

namespace NotificationService.IntegrationTests.Infrastructure;

public class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    ///     Shared secret seeded into <c>Notifications:Unisender:WebhookSecret</c> for the
    ///     test host. The Unisender bounce webhook is fail-closed: without a configured
    ///     secret it returns 401, and with one the <c>X-Unisender-Secret</c> header must
    ///     match this value. Tests POST this header to exercise the happy path; the
    ///     no-secret 401 case is covered by a dedicated factory variant.
    /// </summary>
    public const string UNISENDER_WEBHOOK_SECRET = "test-unisender-webhook-secret";

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("notification_service_db_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection? _dbConnection;

    private string ConnectionString => _dbContainer.GetConnectionString() + ";Search Path=notifications,public";

    public IAuthServiceClient AuthServiceClient { get; } = Substitute.For<IAuthServiceClient>();
    public IEducationContentServiceClient EducationContentClient { get; } = Substitute.For<IEducationContentServiceClient>();
    public IAccessServiceClient AccessServiceClient { get; } = Substitute.For<IAccessServiceClient>();
    public IEmailSender EmailSender { get; } = Substitute.For<IEmailSender>();

    /// <summary>Fake web-push transport — записывает попытки доставки вместо сети (#342).</summary>
    public FakeWebPushSender WebPushSender { get; } = new();

    public TestOutboxCollector OutboxCollector { get; } = new();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Database",
            ConnectionString);

        DbContextOptions<NotificationDbContext> options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using NotificationDbContext dbContext = new(options);

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();

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
        // VAPID-ключи через host config (как ConnectionStrings) — гарантированно видны
        // AddCore при условной регистрации WebPushNotificationChannel (#342). Реальная
        // отправка подменяется FakeWebPushSender в ConfigureTestServices.
        builder.UseSetting("Notifications:WebPush:Subject", "mailto:test@sachkov-learn.net");
        builder.UseSetting(
            "Notifications:WebPush:PublicKey",
            "BLbV9YYJEyU0mxQykMXR7_o7T2SzZtP7ACZu46GuDkdbw20ra2Ys_pzvJdm5YXFOMlNcY2hcB27w6NQ2vpUNCno");
        builder.UseSetting("Notifications:WebPush:PrivateKey", "0QczCmFDYLP5RdJbZNyyrCnadUlH5ny08XyXVqZ_BJQ");

        // Unisender bounce webhook is fail-closed (#430): no secret → 401. Seed a known
        // secret so the happy-path bounce tests can authenticate via X-Unisender-Secret.
        // The NoSecretIntegrationTestsWebFactory variant clears this to cover the 401-when-
        // -unconfigured regression.
        ConfigureUnisenderWebhookSecret(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Tests.json"), optional: true);

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
                ["ConnectionStrings:RabbitMq"] = "amqp://localhost:5672",
                ["DevAuth:Disabled"] = "true",
                ["Authentication:RequireHttpsMetadata"] = "false",
                // Satisfies AccessServiceOptions:Url ValidateOnStart (#444). The real client is
                // replaced by an NSubstitute mock below, so no HTTP call ever uses this URL.
                ["AccessServiceOptions:Url"] = "http://localhost:8010",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<NotificationDbContext>();
            services.AddDbContextPool<NotificationDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
            });

            services.RemoveAll<IAuthServiceClient>();
            services.AddSingleton(AuthServiceClient);

            services.RemoveAll<IEducationContentServiceClient>();
            services.AddSingleton(EducationContentClient);

            services.RemoveAll<IAccessServiceClient>();
            services.AddSingleton(AccessServiceClient);

            services.RemoveAll<IEmailSender>();
            services.AddSingleton(EmailSender);

            // Подменяем реальный VAPID-транспорт фейком (канал зарегистрирован, т.к. ключи
            // заданы в конфиге выше). PushServiceClient остаётся в DI, но не резолвится.
            services.RemoveAll<IWebPushSender>();
            services.AddSingleton<IWebPushSender>(WebPushSender);

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.MetadataAddress = null!;
                options.ConfigurationManager = null;
                options.Configuration = new OpenIdConnectConfiguration();
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

            // Wolverine: stub external transports + envelope persistence. Replace local
            // ITransactionManager + IOutboxService через shared test-doubles из
            // Shared/Wolverine.Testing — durability agent не работает в тестах, поэтому
            // Respawn.ResetAsync больше не ловит 40P01 deadlock. Тесты ассертят publish'и
            // через OutboxCollector.OfType<T>() вместо Wolverine TrackActivity API.
            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();

            services.RemoveAll<ITransactionManager>();
            services.AddSingleton<IReadOnlyDictionary<string, Func<Error>>>(new Dictionary<string, Func<Error>>
            {
                [NotificationsIndex.CORRELATION_UNIQUE] = NotificationErrors.NotificationAlreadyExists,
                [SubscriptionsIndex.USER_ENTITY_UNIQUE] = NotificationErrors.SubscriptionAlreadyExists,
                [NotificationDeliveriesIndex.NOTIFICATION_CHANNEL_UNIQUE] = NotificationErrors.DeliveryAlreadyExists,
            });
            services.AddScoped<ITransactionManager, TestTransactionManager<NotificationDbContext>>();
            services.RemoveAll<IOutboxService>();
            services.AddSingleton(OutboxCollector);
            services.AddScoped<IOutboxService, TestOutboxService>();

            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                List<HealthCheckRegistration> toRemove = options.Registrations
                    .Where(x => string.Equals(x.Name, "rabbitmq", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (HealthCheckRegistration registration in toRemove)
                    options.Registrations.Remove(registration);
            });

            // Rate limit: stub STREAM и BROADCAST policies на NoLimiter, чтобы массовые
            // SSE/broadcast тесты не ловили 429 внутри одного test-run partition'а.
            // PREFERENCES оставляем настоящим limiter'ом — PreferencesRateLimitTests
            // верифицирует 429 boundary через уникальный per-test userId; стаб бы его сломал.
            //
            // RemoveAll<IConfigureOptions<RateLimiterOptions>>() убирает прод-конфиг всех
            // policies; PREFERENCES регистрируется заново с теми же FixedWindow-настройками,
            // что и в prod (см. NotificationRateLimiting.cs).
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.Configure<RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    Error error = Error.Failure(
                        "notifications.rate_limit.exceeded",
                        "Слишком много запросов. Попробуйте позже.");
                    Envelope<object> envelope = Envelope<object>.Fail(error);
                    await context.HttpContext.Response.WriteAsJsonAsync(envelope, cancellationToken);
                };

                options.AddPolicy(
                    NotificationRateLimitPolicies.STREAM,
                    _ => RateLimitPartition.GetNoLimiter(string.Empty));

                options.AddPolicy(
                    NotificationRateLimitPolicies.BROADCAST,
                    _ => RateLimitPartition.GetNoLimiter(string.Empty));

                options.AddPolicy(
                    NotificationRateLimitPolicies.PUSH_REGISTER,
                    _ => RateLimitPartition.GetNoLimiter(string.Empty));

                options.AddPolicy(
                    NotificationRateLimitPolicies.OPEN,
                    httpContext =>
                        RateLimitPartition.GetFixedWindowLimiter(
                            PartitionKey(httpContext),
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 30,
                                Window = TimeSpan.FromMinutes(1),
                                QueueLimit = 0,
                            }));

                // PREFERENCES — оставляем реальный FixedWindow (10/min) для PreferencesRateLimitTests.
                NotificationRateLimitOptions opts = new();
                options.AddPolicy(
                    NotificationRateLimitPolicies.PREFERENCES,
                    httpContext =>
                        RateLimitPartition.GetFixedWindowLimiter(
                            PartitionKey(httpContext),
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = opts.PreferencesUpdatePermitLimit,
                                Window = TimeSpan.FromMinutes(opts.PreferencesUpdateWindowMinutes),
                                QueueLimit = 0,
                            }));
            });
        });
    }

    public async Task ResetDatabaseAsync()
    {
        AuthServiceClient.ClearSubstitute();
        EducationContentClient.ClearSubstitute();
        AccessServiceClient.ClearSubstitute();
        EmailSender.ClearSubstitute();
        WebPushSender.Clear();
        OutboxCollector.Clear();

        if (_dbConnection is null)
            return;

        await _respawner.ResetAsync(_dbConnection);
    }

    /// <summary>
    ///     Seeds the Unisender webhook shared secret into host config. Overridable so a
    ///     factory variant can clear it and exercise the fail-closed (no secret → 401) path.
    /// </summary>
    protected virtual void ConfigureUnisenderWebhookSecret(IWebHostBuilder builder)
    {
        builder.UseSetting("Notifications:Unisender:WebhookSecret", UNISENDER_WEBHOOK_SECRET);
    }

    private async Task InitializeRespawner()
    {
        _respawner = await Respawner.CreateAsync(_dbConnection!,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["notifications"],
            });
    }

    private static string PartitionKey(HttpContext httpContext)
    {
        return httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub")
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
    }
}
