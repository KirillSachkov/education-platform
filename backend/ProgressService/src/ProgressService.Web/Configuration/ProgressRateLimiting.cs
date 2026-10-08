using System.Threading.RateLimiting;
using ProgressService.Core.Features.Courses.Queries;
using ProgressService.Core.Features.LevelTests.UseCases;
using ProgressService.Core.Features.Materials.Queries;
using ProgressService.Core.Features.Materials.UseCases;
using ProgressService.Core.Features.QuizAttempts.UseCases;

namespace ProgressService.Web.Configuration;

public static class ProgressRateLimiting
{
    public static IServiceCollection AddProgressRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(GetCoursePublicStatsEndpoint.ANONYMOUS_READ_RATE_LIMIT_POLICY, httpContext =>
            {
                string partitionKey = httpContext.User.FindFirst("sub")?.Value
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    });
            });

            // Анонимный счётчик просмотров материалов (issue #234) — 60 запросов в минуту
            // на IP, защита от грубой накрутки. Партиционируем по anonymousId из body
            // не получится (нет доступа к body на этапе rate-limit), поэтому только IP.
            options.AddPolicy(RecordAnonymousMaterialViewEndpoint.RATE_LIMIT_POLICY, httpContext =>
            {
                string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    });
            });

            // Анонимный сабмит level-test попытки (issue #479) — 10 запросов в минуту:
            // сабмит дорогой (поход за answer-key + грейдинг + insert), а воронка
            // публичная. Залогиненные партиционируются по sub-claim (не делят bucket
            // за CGNAT/VPN-IP); анонимы — по IP (anonymousId из body на этапе
            // rate-limit недоступен).
            options.AddPolicy(SubmitLevelTestAttemptEndpoint.RATE_LIMIT_POLICY, httpContext =>
            {
                string partitionKey = httpContext.User.FindFirst("sub")?.Value
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    });
            });

            // Batch-чтение счётчиков просмотров (issue #234) — публичный read-эндпоинт,
            // 256 id'ов на запрос. Чуть щедрее на запись, чтобы карусели/фиды на странице
            // не упирались: 120/min per IP.
            options.AddPolicy(GetMaterialViewsCountsEndpoint.RATE_LIMIT_POLICY, httpContext =>
            {
                string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    });
            });

            // «Проверить ответ» на лету в COURSE-квизе (issue #556) — дешевле сабмита, но
            // дёргается часто (по вопросу за клик). Партиционируем по sub-claim, 120/min.
            // Эндпоинт требует Content.VIEW, IP-fallback оставлен на случай отсутствия claim'а.
            options.AddPolicy(CheckQuizQuestionEndpoint.RATE_LIMIT_POLICY, httpContext =>
            {
                string partitionKey = httpContext.User.FindFirst("sub")?.Value
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
