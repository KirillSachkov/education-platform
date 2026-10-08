using System.Data.Common;
using System.Threading.RateLimiting;
using ContentAccess;
using Core.Database;
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
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NSubstitute;
using AccessService.Contracts.HttpCommunication;
using AuthService.Contracts.HttpCommunication;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.Caching.Memory;
using ProgressService.Core.Database;
using ProgressService.Core.Features.Courses.Queries;
using ProgressService.Core.Features.LevelTests.AiGrading;
using ProgressService.Core.Features.LevelTests.UseCases;
using ProgressService.Core.Features.Materials.Queries;
using ProgressService.Core.Features.Materials.UseCases;
using ProgressService.Core.Features.QuizAttempts.UseCases;
using ProgressService.Infrastructure.Postgres;
using Respawn;
using Shared.AI.Skills;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace ProgressService.IntegrationTests.Infrastructure;

public class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("progress_service_db_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection _dbConnection = null!;
    public MockEducationContentServiceClient EducationContentClient { get; } = new();
    public MockAuthServiceClient AuthServiceClient { get; } = new();
    public MockAccessServiceClient AccessServiceClient { get; } = new();
    public FakeUserGrantWriter UserGrantWriter { get; } = new();
    public ContentAccess.TestSupport.FakeEntitlementChecker EntitlementChecker { get; } = CreateGrantAllChecker();

    /// <summary>Скриптуемый AI seam грейдинга level-test'а (ST-5, #480).</summary>
    public FakeLevelTestGradeExtractor GradeExtractor { get; } = new();

    /// <summary>
    ///     Per-test тумблер LevelTestAi:Enabled (handler читает IOptionsSnapshot —
    ///     значение пересобирается на каждый message-scope). Сбрасывается в true
    ///     между тестами в <see cref="ResetDatabaseAsync"/>.
    /// </summary>
    public bool LevelTestAiEnabled { get; set; } = true;

    private static ContentAccess.TestSupport.FakeEntitlementChecker CreateGrantAllChecker()
    {
        var checker = new ContentAccess.TestSupport.FakeEntitlementChecker();
        checker.GrantAll();
        return checker;
    }

    private string ConnectionString => _dbContainer.GetConnectionString() + ";Search Path=progress,public";

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Database",
            ConnectionString);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ProgressDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProgressDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
        await WolverineSchemaHelper.CreateTablesAsync(dbContext);

        _dbConnection = new NpgsqlConnection(ConnectionString);
        await _dbConnection.OpenAsync();
        await InitializeRespawner();
    }

    public new async Task DisposeAsync()
    {
        // Gracefully shut down the host (EF Core pools, Wolverine) BEFORE closing DB connections/containers.
        await base.DisposeAsync();

        if (_dbConnection != null)
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
        // AddOpenAiCompatible валидирует провайдеров в момент регистрации (AddAi):
        // тесты идут в Development env → appsettings.Development.json несёт провайдер
        // aitunnel без ключа — докидываем фейковый ключ через UseSetting (виден
        // конфигурации раньше, чем ConfigureAppConfiguration). Реальный AI в тестах
        // заменён FakeLevelTestGradeExtractor'ом.
        builder.UseSetting("AI:Providers:aitunnel:ApiKey", "test-fake-api-key");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Tests.json"), optional: true);

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
                ["ConnectionStrings:RabbitMq"] = "amqp://localhost:5672",
                ["ConnectionStrings:Redis"] = "localhost:1,abortConnect=false",
                ["DevAuth:Disabled"] = "true",
                // AddOpenAiCompatible валидирует провайдеров на регистрации; тесты идут в
                // Development env → appsettings.Development.json уже несёт провайдер aitunnel
                // без ключа — докидываем фейковый ключ (реальный AI в тестах заменён фейком).
                ["AI:Providers:aitunnel:ApiKey"] = "test-fake-api-key",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ProgressDbContext>();
            services.RemoveAll<ITransactionManager>();
            services.RemoveAll<IDbContextOutbox<ProgressDbContext>>();
            services.RemoveAll<IOutboxService>();
            services.RemoveAll<IEducationContentServiceClient>();
            services.RemoveAll<IAuthServiceClient>();
            services.RemoveAll<IAccessServiceClient>();

            services.AddDbContextPool<ProgressDbContext>((sp, options) =>
            {
                options.UseNpgsql(ConnectionString);
            });

            services.AddScoped<ITransactionManager, TestTransactionManager>();
            services.AddScoped<IOutboxService, NoOpOutboxService>();
            services.AddSingleton<IEducationContentServiceClient>(EducationContentClient);
            services.AddSingleton<IAuthServiceClient>(AuthServiceClient);
            services.AddSingleton<IAccessServiceClient>(AccessServiceClient);

            // Replace Redis with NSubstitute mock (no real Redis in tests)
            services.RemoveAll<IConnectionMultiplexer>();
            var mockRedis = Substitute.For<IConnectionMultiplexer>();
            mockRedis.IsConnected.Returns(true);
            var mockDb = Substitute.For<IDatabase>();
            mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDb);
            services.AddSingleton(mockRedis);

            // Replace IUserGrantWriter with a recording fake so tests can assert Redis calls
            services.RemoveAll<IUserGrantWriter>();
            services.AddSingleton<IUserGrantWriter>(UserGrantWriter);

            // Replace IEntitlementChecker with FakeEntitlementChecker (GrantAll) — иначе
            // mock Redis возвращает false для всех SetCombineAsync, и тесты, которые не
            // про access (bookmarks, MarkMaterialViewed) валятся на entitlement check'е.
            // SubmitIssue capability check всё ещё работает через mock Redis (default false).
            services.RemoveAll<IEntitlementChecker>();
            services.AddSingleton<IEntitlementChecker>(EntitlementChecker);

            // AI seam грейдинга level-test'а (ST-5, #480): closed-generic фейк
            // регистрируется ПОСЛЕ open-generic AddAiSkills из Web → выигрывает
            // при резолве. Скриптуется per-test (успех / failure / throw).
            services.RemoveAll<IStructuredExtractor<LevelTestAiGradesResponse>>();
            services.AddSingleton<IStructuredExtractor<LevelTestAiGradesResponse>>(GradeExtractor);

            // Per-test тумблер Enabled + мгновенные ретраи. Configure-делегат добавлен
            // после Bind() из AddCore → выполняется последним на каждом options-scope.
            services.Configure<LevelTestAiOptions>(options =>
            {
                options.Enabled = LevelTestAiEnabled;
                options.RetryDelaySeconds = 0;
            });

            // Domain event spy — singleton so it survives across HTTP request scopes
            var spy = new DomainEventSpy();
            services.AddSingleton(spy);

            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();
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

            // Replace rate-limit policies with NoLimiter — иначе test suite быстро бьёт
            // лимит и получает 429.
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.Configure<RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = 429;
                string[] policies =
                [
                    GetCoursePublicStatsEndpoint.ANONYMOUS_READ_RATE_LIMIT_POLICY,
                    RecordAnonymousMaterialViewEndpoint.RATE_LIMIT_POLICY,
                    GetMaterialViewsCountsEndpoint.RATE_LIMIT_POLICY,
                    SubmitLevelTestAttemptEndpoint.RATE_LIMIT_POLICY,
                    CheckQuizQuestionEndpoint.RATE_LIMIT_POLICY,
                ];
                foreach (string policy in policies)
                    options.AddPolicy(policy, _ => RateLimitPartition.GetNoLimiter("test"));
            });
        });
    }

    public async Task ResetDatabaseAsync()
    {
        EducationContentClient.Reset();
        AuthServiceClient.Reset();
        UserGrantWriter.Reset();
        GradeExtractor.Reset();
        LevelTestAiEnabled = true;
        // Derive-модель (epic access-derive-model, Phase 1): learning-state / my-enrollment
        // теперь спрашивают entitlement-checker для entitled-but-no-row кейса. Сбрасываем
        // его в дефолтный GrantAll, чтобы DenyAll/DenyResourceType из теста не протекали дальше.
        EntitlementChecker.Reset();
        await _respawner.ResetAsync(_dbConnection);

        // Clear HybridCache L1 (MemoryCache) to prevent stale data across tests
        if (Services.GetService<IMemoryCache>() is MemoryCache mc)
            mc.Compact(1.0);
    }

    private async Task InitializeRespawner()
    {
        _respawner = await Respawner.CreateAsync(_dbConnection,
            new RespawnerOptions { DbAdapter = DbAdapter.Postgres, SchemasToInclude = ["progress"], });
    }
}
