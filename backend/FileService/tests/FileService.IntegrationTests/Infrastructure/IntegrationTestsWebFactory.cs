using System.Data.Common;
using Amazon.S3;
using Core.Database;
using CSharpFunctionalExtensions;
using FileService.Core.Database;
using FileService.Core.Services.AssetRegistry;
using FileService.Infrastructure.Postgres;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NSubstitute;
using Respawn;
using StackExchange.Redis;
using SharedKernel;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Testing;

namespace FileService.IntegrationTests.Infrastructure;

public class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("file_service_db_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly MinioContainer _minioContainer = new MinioBuilder("minio/minio")
        .WithUsername("minioadmin")
        .WithPassword("minioadmin")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection _dbConnection = null!;

    private string ConnectionString => _dbContainer.GetConnectionString() + ";Search Path=files,public";

    public TestOutboxCollector OutboxCollector { get; } = new();

    public ITargetEntityAuthorization TargetEntityAuthorization { get; } =
        Substitute.For<ITargetEntityAuthorization>();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _dbContainer.StartAsync(),
            _minioContainer.StartAsync());

        await EnsureAuthoritativeSchemaStubsAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Database",
            ConnectionString);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        IAmazonS3 s3Client = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
        await TestServiceConfiguration.EnsureBucketsCreatedAsync(s3Client);

        FileServiceDbContext dbContext = scope.ServiceProvider.GetRequiredService<FileServiceDbContext>();
        await dbContext.Database.MigrateAsync();

        _dbConnection = new NpgsqlConnection(ConnectionString);
        await _dbConnection.OpenAsync();
        await InitializeRespawner();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        await _dbConnection.CloseAsync();
        await _dbConnection.DisposeAsync();

        await Task.WhenAll(
            _minioContainer.StopAsync(),
            _dbContainer.StopAsync());

        await _minioContainer.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        OutboxCollector.Clear();
        TargetEntityAuthorization.ClearReceivedCalls();
        TargetEntityAuthorization
            .AuthorizeAsync(Arg.Any<FileService.Domain.TargetEntity>(), Arg.Any<CancellationToken>())
            .Returns(UnitResult.Success<Error>());
        TargetEntityAuthorization
            .AuthorizeManagerAsync(
                Arg.Any<FileService.Domain.TargetEntity>(),
                Arg.Any<CancellationToken>())
            .Returns(UnitResult.Success<Error>());
        await _respawner.ResetAsync(_dbConnection);
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
                ["DevAuth:Disabled"] = "true",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            TestServiceConfiguration.ConfigureDatabase(services, ConnectionString);
            TestServiceConfiguration.ConfigureS3(services, _minioContainer);
            TestServiceConfiguration.ConfigureKinescope(services);

            // Wolverine: stub external transports AND envelope persistence; outbox-flush
            // assertions делаются через TestOutboxCollector вместо Wolverine TrackActivity API.
            // Без DisableAllWolverineMessagePersistence durability agent ловил 40P01 deadlock
            // с Respawn.ResetAsync (старый retry-with-backoff hack удалён вместе с migration на
            // Pattern A — см. docs/agents/wolverine-tests.md).
            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();

            services.RemoveAll<ITransactionManager>();
            services.AddScoped<ITransactionManager, TestTransactionManager<FileServiceDbContext>>();
            services.RemoveAll<IOutboxService>();
            services.AddSingleton(OutboxCollector);
            services.AddScoped<IOutboxService, TestOutboxService>();

            services.RemoveAll<ITargetEntityAuthorization>();
            services.AddSingleton(TargetEntityAuthorization);

            // Replace Redis-backed distributed cache with in-memory (no real Redis in tests)
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            services.RemoveAll<IConnectionMultiplexer>();
            var mockRedis = Substitute.For<IConnectionMultiplexer>();
            var mockDb = Substitute.For<IDatabase>();
            mockRedis.IsConnected.Returns(true);
            mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDb);
            mockDb.PingAsync(Arg.Any<CommandFlags>())
                .Returns(Task.FromResult(TimeSpan.FromMilliseconds(1)));
            services.AddSingleton(mockRedis);

            services.RemoveAll<IHttpClientFactory>();
            var mockHttpClientFactory = Substitute.For<IHttpClientFactory>();
            mockHttpClientFactory.CreateClient(Arg.Any<string>())
                .Returns(_ => TestServiceConfiguration.CreateHealthyHttpClient());
            services.AddSingleton(mockHttpClientFactory);

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
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
                    ValidateIssuerSigningKey = true
                };
            });

            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                var toRemove = options.Registrations.Where(x => x.Name == "rabbitmq").ToList();
                foreach (var registration in toRemove)
                    options.Registrations.Remove(registration);
            });

            // Disable rate-limit policies — replace with NoLimiter so 30/min cap doesn't
            // bite when the suite hammers /files/uploads in the same JWT partition.
            services.RemoveAll<Microsoft.Extensions.Options.IConfigureOptions<
                Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>();
            services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = 429;
                string[] policies = ["file-upload", "asset-bind"];
                foreach (string policy in policies)
                {
                    options.AddPolicy(
                        policy,
                        _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter(string.Empty));
                }
            });
        });
    }

    private async Task InitializeRespawner()
    {
        _respawner = await Respawner.CreateAsync(
            _dbConnection,
            new RespawnerOptions { DbAdapter = DbAdapter.Postgres, SchemasToInclude = ["files"], });
    }

    private async Task EnsureAuthoritativeSchemaStubsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE SCHEMA IF NOT EXISTS auth;
            CREATE SCHEMA IF NOT EXISTS education;
            CREATE TABLE IF NOT EXISTS auth.user_profiles (
                id uuid PRIMARY KEY,
                avatar_id uuid NULL
            );
            CREATE TABLE IF NOT EXISTS education.materials (
                id uuid PRIMARY KEY,
                image_id uuid NULL,
                video_id uuid NULL
            );
            CREATE TABLE IF NOT EXISTS education.courses (
                id uuid PRIMARY KEY,
                image_id uuid NULL,
                video_id uuid NULL
            );
            CREATE TABLE IF NOT EXISTS education.collections (
                id uuid PRIMARY KEY,
                cover_image_id uuid NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }
}
