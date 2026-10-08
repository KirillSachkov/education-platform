using System.Security.Claims;
using System.Threading.RateLimiting;
using AuthService.Core;
using AuthService.Core.Options;
using AuthService.Core.Services;
using AuthService.Infrastructure.Postgres;
using EducationContentService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Shared.Email;
using Shared.Messaging;
using StackExchange.Redis;
using CoreRegistration = AuthService.Core.Registration;

namespace AuthService.Web.Configuration;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// AuthService-specific DI. Cross-cutting wiring (Serilog, OTEL, CORS,
    /// JWT auth, OpenAPI, endpoint discovery, middleware pipeline) comes from
    /// Shared.PlatformBootstrap.AddPlatformDefaults / UsePlatformDefaults.
    /// </summary>
    public static IServiceCollection AddAuthServiceRegistrations(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        string redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
        IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisOptions);
        services.AddSingleton(redis);

        // Persist Data Protection keys to Redis so Identity cookies (CrambleCookie),
        // antiforgery tokens, and OpenIddict client auth properties survive auth-service
        // redeploys and can be shared across horizontally-scaled instances.
        // Without this, every container restart regenerates keys in-memory and invalidates
        // all issued cookies — browser-navigation flows like /auth/github/sync-courses break.
        services.AddDataProtection()
            .PersistKeysToStackExchangeRedis(redis, "auth-service:data-protection-keys")
            .SetApplicationName("auth-service");

        services
            .AddInfrastructurePostgres(configuration)
            .AddCore(configuration)
            .AddEducationServiceHttpCommunication(configuration)
            .AddGitHubOrgHttpCommunication(configuration)
            .AddEndpoints(typeof(CoreRegistration).Assembly);

        services.AddAuthOptions(configuration);
        services.AddIdentityServices(environment);
        services.AddOpenIddict(environment, configuration);
        services.AddAuthRateLimiting();

        services.AddSharedEmailSender(configuration);
        services.AddSingleton<IAuthEmailSender, AuthEmailSender>();
        services.AddScoped<PlatformConfigSyncService>();

        services.AddHealthChecks()
            .AddDbContextCheck<AuthDbContext>("postgresql")
            .AddCheck("redis", new RedisHealthCheck(redis))
            .AddRabbitMqCheck(configuration);

        services.AddHostedService<OpenIddictSeeder>();

        return services;
    }

    private static IServiceCollection AddAuthOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OpenIddictOptions>()
            .BindConfiguration(OpenIddictOptions.SECTION_NAME)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OpenIddictOptions>, OpenIddictOptionsValidator>();

        services.Configure<GitHubOptions>(configuration.GetSection(GitHubOptions.SECTION_NAME));
        services.Configure<SigningKeyOptions>(configuration.GetSection(SigningKeyOptions.SECTION_NAME));
        services.Configure<TelegramLinkOptions>(configuration.GetSection(TelegramLinkOptions.SECTION_NAME));

        services.AddOptions<AuthServiceOptions>()
            .BindConfiguration(AuthServiceOptions.SECTION_NAME)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    private static string GetClientIp(HttpContext httpContext)
        => httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string GetUserPartitionKey(HttpContext httpContext)
    {
        // Authenticated → partition по user id, чтобы юзеры за одним NAT не делили лимит.
        // OpenIddict mappит JWT-claim sub в ClaimTypes.NameIdentifier; зеркалим логику из
        // UserScopedDataMiddleware (сначала NameIdentifier, fallback на raw sub).
        // Anonymous / без identity — fallback на IP.
        string? sub = httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? httpContext.User?.FindFirstValue("sub");
        return !string.IsNullOrEmpty(sub) ? $"user:{sub}" : $"ip:{GetClientIp(httpContext)}";
    }

    private static IServiceCollection AddAuthRateLimiting(
        this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("otp", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(GetClientIp(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10, Window = TimeSpan.FromMinutes(3), QueueLimit = 0,
                    }));

            options.AddPolicy("register", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(GetClientIp(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20, Window = TimeSpan.FromHours(1), QueueLimit = 0,
                    }));

            options.AddPolicy("login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(GetClientIp(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30, Window = TimeSpan.FromMinutes(3), QueueLimit = 0,
                    }));

            options.AddPolicy("github", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(GetClientIp(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0,
                    }));

            options.AddPolicy("password-reset", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(GetClientIp(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5, Window = TimeSpan.FromHours(1), QueueLimit = 0,
                    }));

            // /connect/token — единственный реальный caller это frontend (NextAuth confidential
            // client): и обмен auth-кода, и refresh access-токена идут SERVER-SIDE из одного
            // frontend-контейнера. Браузеры на эндпоинт напрямую не ходят → весь токен-трафик
            // ВСЕЙ платформы приходит с ОДНОГО IP. Per-IP лимит здесь схлопывает всех юзеров в
            // один бакет: при 60/3мин (=20/мин) под нагрузкой класса/когорты эндпоинт начинает
            // отдавать 429, NextAuth-refresh падает → юзеров выбрасывает на error=Configuration,
            // и даже свежий логин (code-exchange) ломается (#592). Держим лимит высоким — один
            // доверенный first-party IP обслуживает всю платформу; brute-force тут невозможен
            // (confidential client + high-entropy refresh-token/код), per-IP остаётся лишь как
            // грубый DoS-потолок. TODO(#592 follow-up): партиционировать per-session
            // (refresh_token/код), а не per-IP, чтобы лимит масштабировался с числом юзеров.
            options.AddPolicy("token", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(GetClientIp(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1000, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                    }));

            options.AddPolicy("anonymous-read", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(GetClientIp(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 60, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0,
                    }));

            // Telegram link-token issuance — authenticated user only. 5 запросов в минуту
            // на юзера. Не на IP — чтобы один пользователь не выбил лимит у соседа за NAT.
            options.AddPolicy("telegram-link", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(GetUserPartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                    }));
        });

        return services;
    }
}
