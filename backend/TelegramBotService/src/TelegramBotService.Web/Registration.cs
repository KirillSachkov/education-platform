using AccessService.Contracts.HttpCommunication;
using ContentAccess.Redis;
using EducationContentService.Contracts.HttpCommunication;
using Shared.Messaging;
using StackExchange.Redis;
using TelegramBotService.Contracts.HttpCommunication;
using TelegramBotService.Core;
using TelegramBotService.Core.Delivery;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.Web.Configuration;

namespace TelegramBotService.Web;

public static class Registration
{
    public static IServiceCollection AddConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        // Redis нужен для capability-чека: F1 invite DM (CourseEnrolledTelegramHandler) пускает
        // юзера в чат только если у него в Redis user-grants есть `cap:COMMUNITY_ACCESS` —
        // FREE LEARN_ONLY план не должен открывать чат. Без cap-тегов (admin/github enroll
        // без plan-grant) handler шлёт DM как раньше — fallback на Source-based legacy flow.
        string redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
        IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisOptions);
        services.AddSingleton(redis);

        services
            .AddCore(configuration)
            .AddInfrastructurePostgres(configuration)
            .AddContentAccessRedis()
            .AddAuthTelegramClient(configuration)
            .AddAccessServiceHttpCommunication(configuration)
            // ECS client — для batch-обогащения course title'ов в /telegram/me/chats; HybridCache
            // декоратор склеивает повторные lookup'ы между запросами.
            .AddEducationServiceHttpCommunication(configuration, enableCaching: true)
            .AddTelegramRateLimiting()
            .AddTelegramDelivery(configuration);

        services.AddHealthChecks()
            .AddDbContextCheck<TelegramBotDbContext>("postgresql")
            .AddRabbitMqCheck(configuration);

        return services;
    }

    /// <summary>
    /// Регистрирует Redis (singleton multiplexer + idempotency store) и
    /// <see cref="ITelegramDeliveryService"/>. Если ConnectionStrings:Redis пуст
    /// (тесты / dev без Redis) — fallback на in-memory store. Multi-replica prod
    /// **обязан** иметь Redis: in-memory store не shared между репликами и не
    /// защищает от дублей.
    /// </summary>
    private static IServiceCollection AddTelegramDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TelegramIdempotencyOptions>()
            .Bind(configuration.GetSection(TelegramIdempotencyOptions.SECTION_NAME))
            .Validate(
                options => options.TtlSeconds is >= 60 and <= 604_800,
                "TelegramIdempotency:TtlSeconds must be between 60 and 604800 seconds.")
            .ValidateOnStart();

        string? redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<ITelegramIdempotencyStore, RedisTelegramIdempotencyStore>();
            services.AddSingleton<IPlanWelcomeSentStore, RedisPlanWelcomeSentStore>();
        }
        else
        {
            // Тесты и dev без Redis. На Production это footgun: in-memory store не shared
            // между репликами → возможны дубли DM. RedisGuardHostedService логирует Critical
            // на старте если environment=Production и Redis не сконфигурирован.
            services.AddSingleton<ITelegramIdempotencyStore, InMemoryTelegramIdempotencyStore>();
            services.AddSingleton<IPlanWelcomeSentStore, InMemoryPlanWelcomeSentStore>();
            services.AddHostedService<RedisIdempotencyGuardHostedService>();
        }

        // Per-chat throttler — ≥1s между sends в один chat (Telegram Bot API DM лимит).
        // Singleton — состояние lastSent должно делиться между всеми scope'ами handler'ов.
        services.AddSingleton<IBotThrottler, PerChatBotThrottler>();

        services.AddScoped<ITelegramDeliveryService, TelegramDeliveryService>();
        return services;
    }
}
