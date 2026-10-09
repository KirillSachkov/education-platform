using System.Data.Common;
using System.Threading.RateLimiting;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Infrastructure.Postgres;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using ContentAccess;
using ContentAccess.TestSupport;
using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using ProgressService.Contracts.HttpCommunication;
using Respawn;
using NSubstitute;
using SharedKernel;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace EducationContentService.IntegrationTests.Infrastructure;

public class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("education_service_db_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection _dbConnection = null!;

    private string ConnectionString => _dbContainer.GetConnectionString() + ";Search Path=education,public";

    public FakeEntitlementChecker EntitlementChecker { get; } = new();

    /// <summary>
    ///     Per-fixture singleton — собирает все integration events, опубликованные
    ///     через <see cref="IOutboxService"/>. Тесты делают
    ///     <c>Factory.OutboxCollector.OfType&lt;MaterialAccessChanged&gt;()</c>.
    /// </summary>
    public TestOutboxService OutboxCollector { get; } = new();

    /// <summary>
    ///     NSubstitute-мок Redis <see cref="IDatabase"/> — write-path access-тегов
    ///     (<c>RedisResourceAccessWriter</c>) пишет в него. L1-тесты sync-handler'ов
    ///     ассертят received-calls (<c>SetAddAsync</c>/<c>KeyDeleteAsync</c>).
    ///     Received-история чистится в <see cref="ResetDatabaseAsync"/>.
    /// </summary>
    public IDatabase RedisDatabase { get; } = Substitute.For<IDatabase>();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Database",
            ConnectionString);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        EducationDbContext dbContext = scope.ServiceProvider.GetRequiredService<EducationDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
        await CreateCommentOwnershipContractViewAsync(dbContext);
        await WolverineSchemaHelper.CreateTablesAsync(dbContext);

        _dbConnection = new NpgsqlConnection(ConnectionString);
        await _dbConnection.OpenAsync();
        await InitializeRespawner();
    }

    private static Task CreateCommentOwnershipContractViewAsync(EducationDbContext dbContext) =>
        dbContext.Database.ExecuteSqlRawAsync("""
            CREATE VIEW education.comment_target_ownership_v1
            WITH (security_barrier = true)
            AS
            WITH ownership_candidates AS (
                SELECT 'course'::text AS target_entity_type,
                    course.id AS target_entity_id,
                    course.author_id,
                    course.id AS course_id,
                    1 AS priority
                FROM education.courses course
                UNION ALL
                SELECT 'material', material.id, course.author_id, binding.course_id, 1
                FROM education.materials material
                JOIN education.course_materials binding ON binding.material_id = material.id
                JOIN education.courses course ON course.id = binding.course_id
                UNION ALL
                SELECT 'material', material.id, course.author_id, course_item.course_id, 2
                FROM education.materials material
                JOIN education.module_items module_item
                    ON module_item.item_type = 'Material'
                    AND module_item.reference_id = material.id
                JOIN education.course_items course_item
                    ON course_item.item_type = 'Module'
                    AND course_item.reference_id = module_item.module_id
                JOIN education.courses course ON course.id = course_item.course_id
                UNION ALL
                SELECT 'material', material.id, material.author_id, NULL::uuid, 3
                FROM education.materials material
                UNION ALL
                SELECT 'issue', issue.id, course.author_id, course_item.course_id, 1
                FROM education.issues issue
                JOIN education.course_items course_item
                    ON course_item.item_type = 'Project'
                    AND course_item.reference_id = issue.project_id
                JOIN education.courses course ON course.id = course_item.course_id
                UNION ALL
                SELECT 'issue', issue.id, course.author_id, course_item.course_id, 2
                FROM education.issues issue
                JOIN education.module_items module_item
                    ON module_item.item_type = 'Issue'
                    AND module_item.reference_id = issue.id
                JOIN education.course_items course_item
                    ON course_item.item_type = 'Module'
                    AND course_item.reference_id = module_item.module_id
                JOIN education.courses course ON course.id = course_item.course_id
                UNION ALL
                SELECT 'issue', issue.id, issue.author_id, NULL::uuid, 3
                FROM education.issues issue
                UNION ALL
                SELECT 'quiz', quiz.id, course.author_id, binding.course_id, 1
                FROM education.quizzes quiz
                JOIN education.course_quizzes binding ON binding.quiz_id = quiz.id
                JOIN education.courses course ON course.id = binding.course_id
                UNION ALL
                SELECT 'quiz', quiz.id, quiz.author_id, NULL::uuid, 2
                FROM education.quizzes quiz
            )
            SELECT DISTINCT ON (target_entity_type, target_entity_id)
                target_entity_type,
                target_entity_id,
                author_id
            FROM ownership_candidates
            ORDER BY target_entity_type, target_entity_id, priority, course_id NULLS LAST;
            """);

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        await _dbConnection.CloseAsync();
        await _dbConnection.DisposeAsync();

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
                ["ConnectionStrings:Redis"] = "localhost:1,abortConnect=false",
                ["Authentication:Authority"] = null,
                ["Authentication:RequireHttpsMetadata"] = "false",
                ["DevAuth:Disabled"] = "true",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<EducationDbContext>();

            services.AddDbContextPool<EducationDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
            });

            // Replace Redis with NSubstitute mock (no real Redis in tests)
            services.RemoveAll<IConnectionMultiplexer>();
            var mockRedis = Substitute.For<IConnectionMultiplexer>();
            IDatabase mockDb = RedisDatabase;
            mockRedis.IsConnected.Returns(true);
            mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDb);
            mockDb.PingAsync(Arg.Any<CommandFlags>())
                .Returns(Task.FromResult(TimeSpan.FromMilliseconds(1)));
            // RedisEntitlementReader.GetUserGrantTagsAsync → SetMembersAsync: default empty set,
            // иначе .Length сломается в prod-коде на null.
            mockDb.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
                .Returns(Array.Empty<RedisValue>());
            services.AddSingleton(mockRedis);

            // Replace IEntitlementChecker with fake
            services.RemoveAll<IEntitlementChecker>();
            services.AddSingleton<IEntitlementChecker>(EntitlementChecker);

            // Replace IFileServiceClient with mock (no real HTTP calls to FileService)
            services.RemoveAll<IFileServiceClient>();
            var mockFileClient = Substitute.For<IFileServiceClient>();
            mockFileClient.GetFileAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success<GetFileResponse?, Error>((GetFileResponse?)null));
            mockFileClient.GetVideoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success<GetVideoResponse?, Error>((GetVideoResponse?)null));
            mockFileClient.GetFilesByEntityAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success<List<GetFileResponse>?, Error>([]));
            mockFileClient.GetFilesBatchAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success<List<GetFileResponse>?, Error>([]));
            mockFileClient.GetVideosBatchAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success<List<GetPublicVideoResponse>?, Error>([]));
            mockFileClient.GetActiveAssetSlotAsync(
                    Arg.Any<string>(),
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Success<GetActiveAssetSlotResponse?, Error>((GetActiveAssetSlotResponse?)null));
            mockFileClient.BindDraftAssetsAsync(Arg.Any<BindDraftAssetsRequest>(), Arg.Any<CancellationToken>())
                .Returns(UnitResult.Success<Error>());
            mockFileClient.SyncEntityAssetsAsync(Arg.Any<SyncEntityAssetsRequest>(), Arg.Any<CancellationToken>())
                .Returns(UnitResult.Success<Error>());
            mockFileClient.BindAssetAsync(Arg.Any<Guid>(), Arg.Any<BindAssetRequest>(), Arg.Any<CancellationToken>())
                .Returns(call => Result.Success<BindAssetResponse, Error>(
                    new BindAssetResponse((long)(uint)call.ArgAt<Guid>(0).GetHashCode() + 1L)));
            mockFileClient.BindAssetInternalAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindAssetInternalRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<BindAssetResponse, Error>(
                new BindAssetResponse((long)(uint)call.ArgAt<Guid>(0).GetHashCode() + 1L)));
            mockFileClient.DetachAssetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(UnitResult.Success<Error>());
            mockFileClient.ReassignAssetsOwnerAsync(
                    Arg.Any<ReassignAssetOwnerRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(UnitResult.Success<Error>());
            services.AddSingleton(mockFileClient);

            // Replace IProgressServiceClient with mock — ECS обогащает Material DTO
            // счётчиком просмотров (issue #234). В тестах counts всегда пустые.
            services.RemoveAll<IProgressServiceClient>();
            var mockProgressClient = Substitute.For<IProgressServiceClient>();
            mockProgressClient.GetMaterialViewsCountsAsync(
                    Arg.Any<IReadOnlyCollection<Guid>>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Success<IReadOnlyDictionary<Guid, long>, Error>(
                    new Dictionary<Guid, long>()));
            services.AddSingleton(mockProgressClient);

            // Replace ICoursePricingClient with mock — no real AccessService HTTP in tests.
            // Default-stub returns empty pricing map (no plans bound to any course).
            services.RemoveAll<EducationContentService.Core.Features.Plans.ICoursePricingClient>();
            var mockPricingClient = Substitute.For<EducationContentService.Core.Features.Plans.ICoursePricingClient>();
            mockPricingClient.GetPlansForCoursesAsync(
                    Arg.Any<IReadOnlyCollection<Guid>>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Success<IReadOnlyDictionary<Guid, EducationContentService.Core.Features.Plans.CoursePricingDto>, Error>(
                    new Dictionary<Guid, EducationContentService.Core.Features.Plans.CoursePricingDto>()));
            services.AddSingleton(mockPricingClient);

            // Replace IAuthorLookupClient with mock — no real AuthService HTTP in tests (#569).
            // Default-stub returns empty author-credit map; author-credit tests override per call.
            services.RemoveAll<EducationContentService.Core.Features.AuthorCredit.IAuthorLookupClient>();
            var mockAuthorClient = Substitute.For<EducationContentService.Core.Features.AuthorCredit.IAuthorLookupClient>();
            mockAuthorClient.GetAuthorsByIdsAsync(
                    Arg.Any<IReadOnlyCollection<Guid>>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Success<IReadOnlyDictionary<Guid, EducationContentService.Core.Features.AuthorCredit.AuthorCreditDto>, Error>(
                    new Dictionary<Guid, EducationContentService.Core.Features.AuthorCredit.AuthorCreditDto>()));
            services.AddSingleton(mockAuthorClient);

            // Override JWT auth with test signing key
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

            // Replace Wolverine-dependent services with simple test implementations
            // so tests don't need RabbitMQ or Wolverine durable persistence
            services.RemoveAll<ITransactionManager>();
            services.AddScoped<ITransactionManager, TestTransactionManager>();
            services.RemoveAll<IOutboxService>();
            services.AddSingleton<IOutboxService>(OutboxCollector);
            services.RemoveAll<IDbContextOutbox<EducationDbContext>>();

            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();

            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                var toRemove = options.Registrations.Where(x => x.Name == "rabbitmq").ToList();
                foreach (var registration in toRemove)
                    options.Registrations.Remove(registration);
            });

            // Rate-limit policies (`anonymous-read`) разбиваются по IP — на parallel-CI
            // все anon-тесты делят `127.0.0.1` и через 60 запросов получают 500 (а не 429)
            // от ASP.NET Core. Заменяем все ECS-policy на NoLimiter, чтобы тесты не флапали.
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.Configure<RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = 429;
                string[] policies = ["anonymous-read"];
                foreach (string policy in policies)
                {
                    options.AddPolicy(policy, _ => RateLimitPartition.GetNoLimiter(string.Empty));
                }
            });
        });
    }

    public async Task ResetDatabaseAsync()
    {
        EntitlementChecker.Reset();
        OutboxCollector.Clear();
        RedisDatabase.ClearReceivedCalls();
        await _respawner.ResetAsync(_dbConnection);

        // Clear HybridCache L1 (MemoryCache) to prevent stale data across tests
        if (Services.GetService<IMemoryCache>() is MemoryCache mc)
            mc.Compact(1.0);
    }

    private async Task InitializeRespawner()
    {
        _respawner = await Respawner.CreateAsync(_dbConnection,
            new RespawnerOptions { DbAdapter = DbAdapter.Postgres, SchemasToInclude = ["education"], });
    }
}