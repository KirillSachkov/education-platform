using System.Data.Common;
using System.Threading.RateLimiting;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using ContentAccess;
using ContentAccess.TestSupport;
using CommentService.Core.Database;
using CommentService.Core.Features.Comments.UseCases;
using Core.Database;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.Materials;
using EducationContentService.Contracts.Ownership;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Npgsql;
using Respawn;
using CommentService.Infrastructure.Postgres;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Testing;

namespace CommentService.IntegrationTests.Infrastructure;

public class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("comment_db_tests")
        .WithUsername("admin")
        .WithPassword("admin")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection _dbConnection = null!;

    private string ConnectionString => _dbContainer.GetConnectionString() + ";Search Path=comments,public";

    public string DatabaseConnectionString => ConnectionString;

    public FakeEntitlementChecker EntitlementChecker { get; } = new();

    public IEducationContentServiceClient EcsClient { get; } = Substitute.For<IEducationContentServiceClient>();

    public TestOutboxCollector OutboxCollector { get; } = new();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        // Wolverine reads ConnectionStrings:Database during Host.ConfigureServices (registration time);
        // env vars must be set BEFORE WebApplicationFactory builds the host. RabbitMq is a syntactically
        // valid amqp URI so RabbitMqHealthCheck can construct a Uri at registration; the actual transport
        // is disabled via DisableAllExternalWolverineTransports below.
        Environment.SetEnvironmentVariable("ConnectionStrings__Database", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", "amqp://localhost:5672");

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        CommentDbContext dbContext = scope.ServiceProvider.GetRequiredService<CommentDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
        await WolverineSchemaHelper.CreateTablesAsync(dbContext);
        await CreateEducationOwnershipSchemaAsync(dbContext);

        _dbConnection = new NpgsqlConnection(ConnectionString);
        await _dbConnection.OpenAsync();
        await InitializeRespawner();
    }

    public new async Task DisposeAsync()
    {
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
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Database", ConnectionString);
        builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://localhost:5672");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Tests.json"), optional: true);

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
                ["ConnectionStrings:RabbitMq"] = "amqp://localhost:5672",
                ["DevAuth:Disabled"] = "true",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CommentDbContext>();

            services.AddDbContextPool<CommentDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
            });

            // Wolverine: stub external transports + envelope persistence. Replace local
            // ITransactionManager + IOutboxService через shared test-doubles из
            // Shared/Wolverine.Testing — durability agent не работает в тестах, поэтому
            // Respawn.ResetAsync больше не ловит 40P01 deadlock. Тесты ассертят publish'и
            // через OutboxCollector.OfType<T>() вместо Wolverine TrackActivity API.
            // RabbitMqHealthCheck конструирует Uri при регистрации → connection string
            // должен быть синтаксически валидным.
            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();

            services.RemoveAll<ITransactionManager>();
            services.AddScoped<ITransactionManager, TestTransactionManager<CommentDbContext>>();
            services.RemoveAll<IOutboxService>();
            services.AddSingleton(OutboxCollector);
            services.AddScoped<IOutboxService, TestOutboxService>();

            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                List<HealthCheckRegistration> toRemove =
                    options.Registrations.Where(x => x.Name is "rabbitmq" or "redis").ToList();
                foreach (HealthCheckRegistration registration in toRemove)
                    options.Registrations.Remove(registration);
            });

            // Replace Redis with NSubstitute mock
            services.RemoveAll<IConnectionMultiplexer>();
            var mockRedis = Substitute.For<IConnectionMultiplexer>();
            services.AddSingleton(mockRedis);

            // Replace IEntitlementChecker with controllable fake
            services.RemoveAll<IEntitlementChecker>();
            services.AddSingleton<IEntitlementChecker>(EntitlementChecker);

            // Replace IEducationContentServiceClient with NSubstitute mock
            services.RemoveAll<IEducationContentServiceClient>();
            ConfigureEcsClientDefaults();
            services.AddSingleton(EcsClient);

            // Replace IAuthServiceClient with NSubstitute mock (returns empty user lists)
            services.RemoveAll<IAuthServiceClient>();
            var mockAuthClient = Substitute.For<IAuthServiceClient>();
            mockAuthClient.GetUsersByIdsAsync(
                Arg.Any<IReadOnlyList<Guid>>(),
                Arg.Any<CancellationToken>())
                .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, SharedKernel.Error>(
                    Array.Empty<AuthUserLookupDto>()));
            services.AddSingleton(mockAuthClient);

            // Override JWT auth with test signing key
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
                    ValidateIssuerSigningKey = true,
                };
            });

            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.Configure<RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy(CreateCommentEndpoint.CREATE_COMMENT_RATE_LIMIT_POLICY, _ =>
                    RateLimitPartition.GetNoLimiter(string.Empty));
            });
        });
    }

    public async Task ResetDatabaseAsync()
    {
        EntitlementChecker.Reset();
        OutboxCollector.Clear();
        ConfigureEcsClientDefaults();
        await _respawner.ResetAsync(_dbConnection);
        await using DbCommand cleanup = _dbConnection.CreateCommand();
        cleanup.CommandText = """
                              TRUNCATE TABLE
                                  education.course_materials,
                                  education.course_quizzes,
                                  education.course_items,
                                  education.module_items,
                                  education.materials,
                                  education.issues,
                                  education.quizzes,
                                  education.courses;
                              """;
        await cleanup.ExecuteNonQueryAsync();
    }

    private static Task CreateEducationOwnershipSchemaAsync(CommentDbContext dbContext) =>
        dbContext.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA IF NOT EXISTS education;
            CREATE TABLE education.courses (
                id uuid PRIMARY KEY,
                author_id uuid NOT NULL
            );
            CREATE TABLE education.materials (
                id uuid PRIMARY KEY,
                author_id uuid NOT NULL
            );
            CREATE TABLE education.issues (
                id uuid PRIMARY KEY,
                author_id uuid NOT NULL,
                project_id uuid NOT NULL
            );
            CREATE TABLE education.quizzes (
                id uuid PRIMARY KEY,
                author_id uuid NOT NULL
            );
            CREATE TABLE education.course_materials (
                course_id uuid NOT NULL,
                material_id uuid NOT NULL,
                PRIMARY KEY (course_id, material_id)
            );
            CREATE TABLE education.course_quizzes (
                course_id uuid NOT NULL,
                quiz_id uuid NOT NULL,
                PRIMARY KEY (course_id, quiz_id)
            );
            CREATE TABLE education.course_items (
                course_id uuid NOT NULL,
                item_type text NOT NULL,
                reference_id uuid NOT NULL
            );
            CREATE TABLE education.module_items (
                module_id uuid NOT NULL,
                item_type text NOT NULL,
                reference_id uuid NOT NULL
            );
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

    private void ConfigureEcsClientDefaults()
    {
        EcsClient.ClearReceivedCalls();
        EcsClient.GetEntityOwnershipAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, SharedKernel.Error>(
                new EntityOwnershipDto(null, null)));
        EcsClient.GetMaterialTitlesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<MaterialTitleDto>, SharedKernel.Error>(
                Array.Empty<MaterialTitleDto>()));
        EcsClient.GetMaterialCourseBindingsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<MaterialCourseBindingLookupDto>, SharedKernel.Error>(
                Array.Empty<MaterialCourseBindingLookupDto>()));
        EcsClient.GetIssueCourseBindingsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<IssueCourseBindingLookupDto>, SharedKernel.Error>(
                Array.Empty<IssueCourseBindingLookupDto>()));
    }

    private async Task InitializeRespawner()
    {
        _respawner = await Respawner.CreateAsync(_dbConnection,
            new RespawnerOptions { DbAdapter = DbAdapter.Postgres, SchemasToInclude = ["comments"], });
    }
}
