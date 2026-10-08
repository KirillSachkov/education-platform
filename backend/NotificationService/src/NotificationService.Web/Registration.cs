using AccessService.Contracts.HttpCommunication;
using AuthService.Contracts.HttpCommunication;
using EducationContentService.Contracts.HttpCommunication;
using Framework.Endpoints;
using NotificationService.Core;
using NotificationService.Core.Sse;
using NotificationService.Infrastructure.Postgres;
using NotificationService.Web.Configuration;
using NotificationService.Web.Sse;
using Shared.Email;
using Shared.Messaging;
using StackExchange.Redis;

namespace NotificationService.Web;

public static class Registration
{
    /// <summary>
    /// Регистрирует service-specific компоненты NotificationService.
    /// Cross-cutting (Serilog/OTel/JWT/CORS/RateLimiter/OpenAPI) приходит из
    /// <see cref="PlatformBootstrapExtensions.AddPlatformDefaults"/>.
    /// </summary>
    public static IServiceCollection AddNotificationServiceRegistrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Нативный браузерный EventSource (SSE) не шлёт Authorization header — токен
        // приходит query-параметром `access_token` строго для /notifications/stream (#457).
        services.AddSseQueryStringTokenAuth();

        services
            .AddEndpoints(typeof(NotificationService.Core.Registration).Assembly)
            .AddEndpoints(typeof(Registration).Assembly);

        // Реальный SSE hub + фоновый heartbeat. Регистрируем ДО AddCore, чтобы
        // TryAddSingleton<ISseConnectionHub, NullSseConnectionHub> в Core стал no-op.
        services.AddSingleton<SseConnectionHub>();
        services.AddSingleton<ISseConnectionHub>(sp => sp.GetRequiredService<SseConnectionHub>());
        services.AddHostedService<SseHeartbeatService>();

        // SSE Redis fan-out — включается когда NotificationService деплоится в ≥2 реплики.
        // Single-replica (default): NullSseRedisPublisher (no-op) → SseFanoutHandler пушит в локальный hub.
        services.Configure<SseOptions>(configuration.GetSection(SseOptions.SECTION_NAME));
        SseOptions sseOptions =
            configuration.GetSection(SseOptions.SECTION_NAME).Get<SseOptions>() ?? new SseOptions();
        string? redisConn = configuration.GetConnectionString("Redis");

        if (sseOptions.RedisFanoutEnabled && string.IsNullOrWhiteSpace(redisConn))
        {
            throw new InvalidOperationException(
                "Sse:RedisFanoutEnabled requires ConnectionStrings:Redis; "
                + "falling back to local fan-out would lose realtime notifications across replicas.");
        }

        if (sseOptions.RedisFanoutEnabled)
        {
            ConfigurationOptions redisOpts = ConfigurationOptions.Parse(redisConn!);
            redisOpts.AbortOnConnectFail = false;
            services.AddSingleton<IConnectionMultiplexer>(
                _ => ConnectionMultiplexer.Connect(redisOpts));
            services.AddSingleton<ISseRedisPublisher, RedisSseFanoutPublisher>();
            services.AddHostedService<SseRedisSubscriberService>();
        }
        else
        {
            services.AddSingleton<ISseRedisPublisher, NullSseRedisPublisher>();
        }

        services
            .AddCore(configuration)
            .AddInfrastructurePostgres(configuration)
            // enableCaching=true → HybridCache decorator на lookup-методы.
            // Critical для Email N+1 (auth user lookup кешируется на батчах) и enrichment'а
            // (course/issue title — высокий hit ratio при том же курсе → много нотификаций).
            .AddEducationServiceHttpCommunication(configuration, enableCaching: true)
            .AddAuthServiceHttpCommunication(configuration, enableCaching: true)
            // AccessService client — нужен SubscribeLifetimeGranteesOnCourseCreatedHandler
            // для batch-lookup юзеров с активным LIFETIME_ALL grant'ом автора (issue #80)
            // и SubscribeOnPlanGrantCreatedHandler для covered-courses fan-out.
            .AddAccessServiceHttpCommunication(configuration);

        // Fail-fast на пустой/битый AccessServiceOptions.Url. Раньше пустой URL молча проходил
        // DI и валил очередь notifications.access.grant_events UriFormatException'ом (new Uri("")
        // в AddAccessServiceHttpCommunication при первом резолве клиента) — весь PlanGrantCreated
        // envelope уходил в retry/dead-letter, и после оплаты юзер не получал ни уведомление о
        // доступе, ни welcome-email. Лучше не стартовать сервис, чем молча терять уведомления (#444).
        services.AddOptions<AccessServiceOptions>()
            .Validate(
                options => Uri.TryCreate(options.Url, UriKind.Absolute, out _),
                "AccessServiceOptions:Url должен быть непустым абсолютным URI (например http://access-service:8010).")
            .ValidateOnStart();

        services.AddSharedEmailSender(configuration);

        services.AddNotificationRateLimiting(configuration);

        services.AddHealthChecks()
            .AddDbContextCheck<NotificationDbContext>("postgresql")
            .AddRabbitMqCheck(configuration);

        return services;
    }

    /// <summary>
    /// Маппит NotificationService endpoints. Стандартный pipeline (forwarded headers,
    /// CORS, exception, request correlation, rate limiter, JWT, request logging,
    /// OpenAPI+Scalar, /health) уже навешен <see cref="PlatformBootstrapExtensions.UsePlatformDefaults"/>.
    /// </summary>
    public static WebApplication MapNotificationEndpoints(this WebApplication app)
    {
        // SSE endpoint требует группу с DisableAntiforgery — иначе MVC anti-forgery
        // блокирует EventSource long-poll connection.
        RouteGroupBuilder apiGroup = app.MapGroup(string.Empty).DisableAntiforgery();
        app.MapEndpoints(apiGroup);

        return app;
    }
}
