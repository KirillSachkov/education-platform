using Npgsql;
using SharedKernel.Exceptions;
using Wolverine;
using Wolverine.ErrorHandling;

namespace Shared.Messaging;

public static class WolverineErrorHandlingExtensions
{
    public static void ConfigureStandardErrorPolicies(this WolverineOptions opts)
    {
        // 1. ИНФРАСТРУКТУРНЫЕ СБОИ (Transient Errors)
        // ---------------------------------------------------------------------
        // Ошибки, которые могут исчезнуть сами собой через короткое время.
        // Стратегия: "Быстро попробовать в памяти, затем надежно запланировать через БД".
        opts.Policies.OnException<TransientException>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(3))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(10));

        // Обработка временных ошибок PostgreSQL (дедлоки, разрывы соединения)
        opts.Policies.OnException<NpgsqlException>(ex => ex.IsTransient)
            .RetryWithCooldown(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(3))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(10));

        // Обработка таймаутов (сеть, внешние API)
        opts.Policies.OnException<TimeoutException>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(3))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(15));

        // Обработка временных проблем внешних HTTP-интеграций
        opts.Policies.OnException<HttpRequestException>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(3))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(15));

        // Часто приходит из HttpClient как таймаут (не пользовательская отмена)
        opts.Policies.OnException<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
            .RetryWithCooldown(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(3))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(15));

        opts.Policies.OnException<IOException>()
            .ScheduleRetry(TimeSpan.FromSeconds(1))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(5));

        // ---------------------------------------------------------------------
        // 2. БИЗНЕС-ОШИБКИ И ВАЛИДАЦИЯ (Permanent Errors)
        // ---------------------------------------------------------------------
        // Ошибки, которые НИКОГДА не пройдут при повторе (кривые данные, баги кода).
        // Стратегия: "Fail Fast". Сразу отправляем в Dead Letter Queue (.error очередь),
        // чтобы не тратить ресурсы и не засорять логи бесконечными попытками.
        opts.Policies.OnException<PermanentException>().MoveToErrorQueue();

        opts.Policies.OnException<ArgumentException>().MoveToErrorQueue();
        opts.Policies.OnException<NotImplementedException>().MoveToErrorQueue();
        opts.Policies.OnException<InvalidCastException>().MoveToErrorQueue();

        // ---------------------------------------------------------------------
        // 3. ПОЛИТИКА ПО УМОЛЧАНИЮ (Fallback)
        // ---------------------------------------------------------------------
        // "Сетка безопасности" для всех остальных непредвиденных исключений (NullReference и т.д.).
        // Мы не знаем, transient это или permanent, поэтому пробуем аккуратно.
        opts.Policies.OnException<Exception>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(5))
            .Then.MoveToErrorQueue();
    }
}
