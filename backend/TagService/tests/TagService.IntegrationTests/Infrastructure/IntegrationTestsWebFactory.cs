using System.Data.Common;
using System.Threading.RateLimiting;
using Core.Database;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Ownership;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
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
using StackExchange.Redis;
using TagService.Core;
using TagService.Core.Database;
using TagService.Infrastructure.Postgres;
using TagService.Infrastructure.Postgres.Database;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Testing;

namespace TagService.IntegrationTests.Infrastructure;

public class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tag_service_db_tests")
        .WithUsername("admin")
        .WithPassword("admin")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection? _dbConnection;
    private string ConnectionString => _dbContainer.GetConnectionString() + ";Search Path=tags,public";

    public string DatabaseConnectionString => ConnectionString;

    public IEducationContentServiceClient EducationContentClient { get; } = Substitute.For<IEducationContentServiceClient>();

    public TestOutboxCollector OutboxCollector { get; } = new();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Database",
            ConnectionString);

        DbContextOptions<TagDbContext> options = new DbContextOptionsBuilder<TagDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using TagDbContext dbContext = new(options);

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
        await WolverineSchemaHelper.CreateTablesAsync(dbContext);

        _dbConnection = new NpgsqlConnection(ConnectionString);
        await _dbConnection.OpenAsync();
        await InitializeRespawner();
    }

    public new async Task DisposeAsync()
    {
        // Wolverine agents may cancel in-flight tasks during host shutdown;
        // we don't care about those exceptions during teardown.
        try { await base.DisposeAsync(); } catch (OperationCanceledException) { /* shutdown-race, intentional */ }

        if (_dbConnection is not null)
        {
            try { await _dbConnection.CloseAsync(); } catch (OperationCanceledException) { /* shutdown-race, intentional */ }
            await _dbConnection.DisposeAsync();
        }

        try { await _dbContainer.StopAsync(); } catch (OperationCanceledException) { /* shutdown-race, intentional */ }
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
                ["ConnectionStrings:Redis"] = "localhost:1,abortConnect=false",
                ["ConnectionStrings:RabbitMq"] = "amqp://localhost:5672",
                ["Authentication:Authority"] = null,
                ["Authentication:RequireHttpsMetadata"] = "false",
                ["DevAuth:Disabled"] = "true",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TagDbContext>();

            services.AddDbContextPool<TagDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
            });

            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            services.RemoveAll<IEducationContentServiceClient>();
            services.AddSingleton(EducationContentClient);

            services.RemoveAll<IConnectionMultiplexer>();
            var mockRedis = Substitute.For<IConnectionMultiplexer>();
            var mockDb = Substitute.For<IDatabase>();
            mockRedis.IsConnected.Returns(true);
            mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDb);
            mockDb.PingAsync(Arg.Any<CommandFlags>())
                .Returns(Task.FromResult(TimeSpan.FromMilliseconds(1)));
            services.AddSingleton(mockRedis);

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

            // Wolverine: stub external transports AND envelope persistence; outbox-flush
            // assertions go through TestOutboxCollector. Without DisableAllWolverineMessagePersistence
            // the durability agent races Respawn.ResetAsync and triggers 40P01 deadlock.
            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();

            services.RemoveAll<ITransactionManager>();
            services.AddScoped<ITransactionManager>(sp =>
                new TestTransactionManager<TagDbContext>(
                    sp.GetRequiredService<TagDbContext>(),
                    uniqueConstraintMap: TransactionManager.UniqueConstraintMap));
            services.RemoveAll<IOutboxService>();
            services.AddSingleton(OutboxCollector);
            services.AddScoped<IOutboxService, TestOutboxService>();

            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                var toRemove = options.Registrations.Where(x => x.Name == "rabbitmq").ToList();
                foreach (var registration in toRemove)
                    options.Registrations.Remove(registration);
            });

            // Rate limit: replace prod policies (60/min sliding-window) with NoLimiter.
            // Without this the test suite quickly hits the cap and gets 429 → false failures.
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.Configure<RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = 429;
                string[] policies =
                [
                    Constants.ANONYMOUS_READ_RATE_LIMIT_POLICY,
                ];
                foreach (string policy in policies)
                {
                    options.AddPolicy(policy, _ => RateLimitPartition.GetNoLimiter(""));
                }
            });
        });
    }

    public async Task ResetDatabaseAsync()
    {
        EducationContentClient.ClearReceivedCalls();
        EducationContentClient.GetEntityOwnershipAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, SharedKernel.Error>(
                new EntityOwnershipDto(null, null)));
        OutboxCollector.Clear();

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
                SchemasToInclude = ["tags"],
            });
    }
}
