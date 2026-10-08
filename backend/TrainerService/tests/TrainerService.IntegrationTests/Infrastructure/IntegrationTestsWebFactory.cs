using System.Data.Common;
using System.Threading.RateLimiting;
using ContentAccess;
using ContentAccess.TestSupport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Npgsql;
using Respawn;
using Shared.AI;
using StackExchange.Redis;
using TrainerService.Core.Features.Sessions.Grading;
using TrainerService.Core.Features.Stats.UserLookup;
using TrainerService.Infrastructure.Postgres;
using TrainerService.Web;
using Testcontainers.PostgreSql;

namespace TrainerService.IntegrationTests.Infrastructure;

/// <summary>
/// Postgres testcontainer + Respawn. TrainerService has NO Wolverine and NO Redis in Ф1, and after
/// #623 owns its question bank locally (no ECS), so there is no outbox/persistence wiring to disable
/// and no S2S dependency to mock. Questions are seeded directly via <see cref="TrainerQuestionFixtures"/>.
/// The AI clients + open-answer grader + entitlement checker + quota Redis are faked. JWT validation
/// is pointed at <see cref="TestJwtHelper"/>.
/// </summary>
public sealed class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("trainer_testdb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection _dbConnection = null!;

    /// <summary>
    /// Controllable LLM client (#585) — open-answer grading + aggregate mock feedback. Registered
    /// scoped (overriding the singleton) so the re-created instance from <see cref="ResetDatabaseAsync"/>
    /// is picked up per scope. Tests stub <c>GenerateAsync&lt;JsonElement&gt;</c>.
    /// </summary>
    public IAiClient AiClient { get; private set; } = Substitute.For<IAiClient>();

    /// <summary>Controllable STT client (#585) — Whisper transcription of spoken answers.</summary>
    public IAiTranscriptionClient AiTranscription { get; private set; } =
        Substitute.For<IAiTranscriptionClient>();

    /// <summary>
    ///     Fake open-answer grader (#568 W2) — no real LLM. Replaces the production
    ///     <c>IOpenAnswerGrader</c> so both the inline non-mock CheckAnswer path AND the mock
    ///     background path are graded deterministically. Tests configure the verdict/score/feedback
    ///     or flip <c>FailNext</c> / use the throw-sentinel to exercise the fallback. Re-created per
    ///     test in <see cref="ResetDatabaseAsync"/>.
    /// </summary>
    public FakeOpenAnswerGrader OpenAnswerGrader { get; private set; } = new();

    /// <summary>
    ///     Controllable entitlement checker (#614) — gates <c>cap:TRAINER_PRO</c> (PAID banks /
    ///     voice / mock). Created ONCE and registered as the DI singleton; <see cref="ResetDatabaseAsync"/>
    ///     mutates its state (back to GrantAll baseline) rather than re-creating it, so the DI link
    ///     stays intact. Default baseline = GrantAll (PRO); free-tier tests call <c>DenyAll()</c>.
    /// </summary>
    public FakeEntitlementChecker EntitlementChecker { get; } = new();

    /// <summary>
    ///     In-memory counting Redis for the quota gate (#614 C2). Registered as the DI singleton;
    ///     counters cleared per test in <see cref="ResetDatabaseAsync"/> so a per-user-per-day count
    ///     doesn't leak between tests.
    /// </summary>
    public FakeQuotaRedis QuotaRedis { get; } = new();

    /// <summary>
    ///     Deterministic AuthService user-lookup (#681 T2) — replaces the real HTTP client so admin
    ///     top-user enrichment (display name + avatar) is controllable. Created once + registered as the
    ///     DI singleton; <see cref="ResetDatabaseAsync"/> resets its state so configured users / the
    ///     throw-flag don't leak between tests.
    /// </summary>
    public FakeUserLookupClient UserLookup { get; } = new();

    /// <summary>
    ///     Low per-user quota limits + a small audio cap injected for the #614 C2 tests. These are set in
    ///     the in-memory config so a Free user hits the open-grade cap quickly, a Pro dimension can be
    ///     proven unlimited (0), and a small <c>MaxAudioBytes</c> makes the audio-cap test post a tiny file.
    ///     Pro open-grades + mock stay unlimited (0) so the other suites (PRO/admin sessions) never trip.
    ///     <list type="bullet">
    ///         <item>Free OpenGradesPerDay = 3 (low + testable), VoiceMinutes = 0, Mock = 0;</item>
    ///         <item>Pro OpenGradesPerDay = 0 (unlimited), VoiceMinutesPerMonth = 5, MockPerMonth = 0 (unlimited);</item>
    ///         <item>MaxAudioBytes = 4096 (tiny — the 2 KiB fixtures pass, an 8 KiB file trips too_long);</item>
    ///         <item>MaxVoiceAnswerSeconds = 200 (allows the duration-metering fixtures).</item>
    ///     </list>
    ///     VOICE is metered in audio MINUTES (#663): the budget is 5 min = 300s, so a 200s answer consumes 4 min.
    /// </summary>
    public const int TestFreeOpenGradesPerDay = 3;
    public const int TestProVoiceMinutesPerMonth = 5;
    public const int TestMaxVoiceAnswerSeconds = 200;
    public const long TestMaxAudioBytes = 4096;

    private string ConnectionString => _pgContainer.GetConnectionString() + ";Search Path=trainer,public";

    public async Task InitializeAsync()
    {
        await _pgContainer.StartAsync();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrainerServiceDbContext>();
        await db.Database.MigrateAsync();

        _dbConnection = new NpgsqlConnection(ConnectionString);
        await _dbConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(
            _dbConnection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["trainer"],
            });
    }

    /// <summary>Resets the DB and re-creates clean AI mocks so per-test stubs don't leak.</summary>
    public async Task ResetDatabaseAsync()
    {
        AiClient = Substitute.For<IAiClient>();
        AiTranscription = Substitute.For<IAiTranscriptionClient>();
        OpenAnswerGrader = new FakeOpenAnswerGrader();
        EntitlementChecker.GrantAll(); // reset state on the same DI-registered instance (PRO baseline)
        QuotaRedis.Reset();            // clear quota counters so per-user-per-day counts don't leak
        UserLookup.Reset();            // clear configured users + throw-flag (#681 T2)
        await _respawner.ResetAsync(_dbConnection);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        if (_dbConnection is not null)
        {
            await _dbConnection.CloseAsync();
            await _dbConnection.DisposeAsync();
        }

        await _pgContainer.StopAsync();
        await _pgContainer.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" (not Development) so the DevAuth bypass middleware never runs and the
        // test JWT is the actual auth. DevAuth:Disabled is belt-and-suspenders.
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Database", ConnectionString);

        // AI provider config is validated eagerly in AddOpenAiCompatible (empty ApiKey → throws at
        // startup, before ConfigureTestServices swaps the mocks). Set via UseSetting so it wins over
        // appsettings.json's empty ApiKey at the moment Program.cs reads it. The IAiClient/
        // IAiTranscriptionClient are mocked below, so this key is never actually used for a call.
        builder.UseSetting("AI:Default", "aitunnel");
        builder.UseSetting("AI:Providers:aitunnel:Kind", "OpenAiCompatible");
        builder.UseSetting("AI:Providers:aitunnel:BaseUrl", "https://api.aitunnel.ru/v1/");
        builder.UseSetting("AI:Providers:aitunnel:ApiKey", "test-key");
        builder.UseSetting("AI:Providers:aitunnel:TimeoutSeconds", "900");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
                ["DevAuth:Disabled"] = "true",
                ["Authentication:Authority"] = null,
                ["Authentication:RequireHttpsMetadata"] = "false",
                // Low per-user quota limits + small audio cap for the #614 C2 tests (see the constants above).
                ["TrainerAI:Limits:Free:OpenGradesPerDay"] = TestFreeOpenGradesPerDay.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["TrainerAI:Limits:Free:VoiceMinutesPerMonth"] = "0",
                ["TrainerAI:Limits:Free:MockPerMonth"] = "0",
                ["TrainerAI:Limits:Pro:OpenGradesPerDay"] = "0",
                ["TrainerAI:Limits:Pro:VoiceMinutesPerMonth"] = TestProVoiceMinutesPerMonth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["TrainerAI:Limits:Pro:MockPerMonth"] = "0",
                ["TrainerAI:MaxAudioBytes"] = TestMaxAudioBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["TrainerAI:MaxVoiceAnswerSeconds"] = TestMaxVoiceAnswerSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["TrainerAI:OpenGradeRateLimitPerMinute"] = "1000",
                // #681 T2: satisfy AuthServiceOptions binding at startup (the real client is replaced by
                // FakeUserLookupClient below, so this URL is never actually called).
                ["AuthService:Url"] = "http://auth-service:8005/",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace the real AI clients (#585) with controllable NSubstitute mocks. Registered
            // scoped (overriding the production singletons) so the per-test re-created instances from
            // ResetDatabaseAsync are read per scope — same trick as the ECS client above. The grader
            // resolves IAiClient from its own background scope, so scoped works there too.
            services.RemoveAll<IAiClient>();
            services.AddScoped(_ => AiClient);
            services.RemoveAll<IAiTranscriptionClient>();
            services.AddScoped(_ => AiTranscription);

            // Replace the production OpenAnswerGrader (real LLM) with the controllable fake (#568 W2)
            // so both inline non-mock CheckAnswer grading and the mock background path are
            // deterministic without hitting an AI provider. Same scoped per-test swap as above.
            services.RemoveAll<IOpenAnswerGrader>();
            services.AddScoped<IOpenAnswerGrader>(_ => OpenAnswerGrader);

            // Replace the Redis-backed entitlement checker (#614) with a controllable fake so the
            // cap:TRAINER_PRO gate (PAID banks / voice / mock) is deterministic without a real Redis.
            services.RemoveAll<IEntitlementChecker>();
            services.AddSingleton<IEntitlementChecker>(EntitlementChecker);

            // Quota counters (#614 C2) talk to Redis directly. Swap the bare substitute for an in-memory
            // counting fake so a cap actually trips (StringIncrement/Decrement back a dictionary).
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(QuotaRedis.Multiplexer);

            // Replace the AuthService HTTP user-lookup (#681 T2) with the deterministic fake so admin
            // top-user enrichment is controllable without a live AuthService (and no real S2S call).
            services.RemoveAll<IUserLookupClient>();
            services.AddSingleton<IUserLookupClient>(UserLookup);

            // No Wolverine in Ф1 → no rabbitmq transport, but the template may still register a
            // health check. Strip it defensively so /health doesn't report Unhealthy in tests.
            services.PostConfigure<HealthCheckServiceOptions>(opts =>
            {
                List<HealthCheckRegistration> toRemove =
                    opts.Registrations.Where(x => x.Name == "rabbitmq").ToList();
                foreach (HealthCheckRegistration r in toRemove)
                    opts.Registrations.Remove(r);
            });

            // Swap every rate-limit policy the service uses for a NoLimiter — otherwise the suite
            // quickly trips the limit (429) or, if a policy were missing, 500s. Add new policy names
            // here when the service adds them (#585: trainer-transcribe; #681: admin-stats).
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.Configure<RateLimiterOptions>(options =>
            {
                options.RejectionStatusCode = 429;
                string[] policies = ["trainer-transcribe", "admin-stats"];
                foreach (string p in policies)
                    options.AddPolicy(p, _ => RateLimitPartition.GetNoLimiter(""));
            });

            // Validate the test JWT with the TestJwtHelper signing key — no real OIDC discovery.
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
}
