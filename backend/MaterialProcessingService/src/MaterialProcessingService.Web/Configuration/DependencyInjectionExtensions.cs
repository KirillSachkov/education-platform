using System.Threading.RateLimiting;
using FileService.Contracts.HttpCommunication;
using EducationContentService.Contracts.HttpCommunication;
using Framework.Cors;
using Framework.Endpoints;
using Observability;
using PlatformAuth;
using PlatformAuth.Authorization;
using Shared.Messaging;
using MaterialProcessingService.Core;
using MaterialProcessingService.Infrastructure.FFmpeg;
using MaterialProcessingService.Infrastructure.AI.Configuration;
using MaterialProcessingService.Infrastructure.Postgres;
using MaterialProcessingService.Web.Jobs;

namespace MaterialProcessingService.Web.Configuration;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers();

        services
            .AddFrameworkCors(configuration)
            .AddSerilogLogging(configuration, "MaterialProcessingService")
            .AddObservability(configuration, "MaterialProcessingService")
            .AddOpenApiWithAuth()
            .AddEndpoints(typeof(MaterialProcessingService.Core.DependencyInjectionExtensions).Assembly)
            .AddJwtAuthentication(configuration)
            .AddEducationServiceHttpCommunication(configuration)
            .AddFileServiceHttpCommunication(configuration)
            .AddCore(configuration)
            .AddInfrastructurePostgres(configuration)
            .AddFFmpeg(configuration)
            .AddAiGeneration(configuration);

        services.AddHealthChecks()
            .AddDbContextCheck<MaterialProcessingServiceDbContext>("postgresql")
            .AddRabbitMqCheck(configuration);

        // StuckJobSweeper — без него OOM-killed jobs остаются в PROCESSING навсегда,
        // partial unique index `ux_timecode_jobs_video_active` блокирует любой новый
        // job на том же videoId → автор не может retry. См. comments в StuckJobSweeper.cs.
        services.AddOptions<StuckJobSweeperOptions>()
            .Bind(configuration.GetSection(StuckJobSweeperOptions.SECTION_NAME))
            .Validate(options => options.StuckThreshold >= TimeSpan.FromMinutes(45)
                                 && options.StuckThreshold <= TimeSpan.FromHours(24),
                "StuckJobSweeper:StuckThreshold must be between 45 minutes and 24 hours")
            .Validate(options => options.SweepInterval >= TimeSpan.FromSeconds(30)
                                 && options.SweepInterval <= TimeSpan.FromHours(1),
                "StuckJobSweeper:SweepInterval must be between 30 seconds and 1 hour")
            .Validate(options => options.BatchSize is >= 1 and <= 1000,
                "StuckJobSweeper:BatchSize must be between 1 and 1000")
            .ValidateOnStart();
        services.AddHostedService<StuckJobSweeper>();

        // Rate-limit на AI-trigger эндпоинты. Без этого один автор с валидным токеном
        // мог последовательно (idempotency блокирует только параллельные на тот же
        // videoId) вылить десятки тысяч ₽ в AI: 60 видео × 60-min × ~30 ₽ = ~1800 ₽
        // на одного юзера в час. Лимит 6 enqueue/час суммарно через все 3 generate
        // эндпоинта — реалистичный потолок «автор обновляет курс за смену».
        // RejectionStatusCode=429, без очереди — клиент сразу получает отказ
        // и понимает, что его задушили.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;

            options.AddPolicy("ai-generation", httpContext =>
            {
                string partitionKey = httpContext.User.FindFirst("sub")?.Value
                    ?? httpContext.User.Identity?.Name
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anon";

                // Admin (platform-admin role) bypass'ит лимит — bulk-backfill через
                // mcp-admin client_credentials и ручные admin-операции не должны
                // упираться в потолок 6/час, рассчитанный на одного автора.
                bool isAdmin = httpContext.User.FindAll("roles")
                    .Any(c => string.Equals(c.Value, PlatformRoles.ADMIN, StringComparison.Ordinal));
                if (isAdmin)
                    return RateLimitPartition.GetNoLimiter(partitionKey);

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 6,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
