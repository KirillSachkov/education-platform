using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core;
using NotificationService.Core.Features.Digest;

namespace NotificationService.Infrastructure.Postgres.Digest;

/// <summary>
/// Фоновый сервис еженедельного дайджеста (#468). Раз в
/// <see cref="NotificationDigestOptions.CheckIntervalMinutes"/> минут проверяет, не пора ли
/// разослать «что нового за неделю», и при попадании в слот зовёт
/// <see cref="IWeeklyDigestRunner.RunOnceAsync"/>.
///
/// <para>
/// Реализация зеркалит <see cref="Retention.NotificationRetentionService"/>:
/// <list type="bullet">
///   <item>Enable/disable через конфиг (<c>Notifications:Digest:Enabled</c>), без пересборки.</item>
///   <item>Initial delay — не конкурируем с migrate + startup.</item>
///   <item>Scoped DI: scope на каждый проход, DbContext не живёт вечно.</item>
///   <item>Resilient: исключение одного цикла логируется, следующий тик проходит штатно.</item>
/// </list>
/// </para>
///
/// <para>
/// Watermark без миграции: последний глобальный запуск = <c>MAX(created_at)</c> уведомлений
/// <c>type = WeeklyDigest</c> (см. <see cref="IWeeklyDigestRunner.GetLastGlobalRunAtAsync"/>).
/// Guard: запускаемся только если now ≥ последнего слота расписания (слот строится как
/// ближайшее прошедшее DayOfWeekUtc@HourUtc) И последний запуск был ≥ 6 дней назад
/// (либо запусков ещё не было). Ручной прогон через admin-endpoint guard не проходит —
/// он зовёт runner напрямую.
/// </para>
/// </summary>
public sealed class WeeklyDigestService : BackgroundService
{
    /// <summary>
    /// Минимальный интервал между авто-запусками. 6 (не 7) дней — допуск на дрейф
    /// фактического времени запуска между неделями.
    /// </summary>
    private const int MIN_DAYS_BETWEEN_RUNS = 6;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<NotificationOptions> _options;
    private readonly ILogger<WeeklyDigestService> _logger;

    public WeeklyDigestService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<NotificationOptions> options,
        ILogger<WeeklyDigestService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NotificationDigestOptions cfg = _options.CurrentValue.Digest;

        if (!cfg.Enabled)
        {
            _logger.LogInformation("Weekly digest disabled; background service exiting.");
            return;
        }

        // Отсрочка первого запуска, чтобы не конкурировать с migrate + startup.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(cfg.InitialDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using PeriodicTimer timer = new(TimeSpan.FromMinutes(Math.Max(1, cfg.CheckIntervalMinutes)));

        // Первый чек сразу (после initial delay), затем по таймеру.
        await RunScheduledCycleAsync(stoppingToken);

        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    return;

                await RunScheduledCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // Один цикл упал — логируем и ждём следующий тик. Не убиваем сервис.
                _logger.LogError(ex, "Weekly digest cycle failed; continuing on next interval");
            }
        }
    }

    private async Task RunScheduledCycleAsync(CancellationToken ct)
    {
        NotificationDigestOptions cfg = _options.CurrentValue.Digest;
        DateTime nowUtc = DateTime.UtcNow;
        DateTime slotUtc = MostRecentScheduledSlotUtc(nowUtc, cfg.DayOfWeekUtc, cfg.HourUtc);

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IWeeklyDigestRunner runner = scope.ServiceProvider.GetRequiredService<IWeeklyDigestRunner>();

        DateTime? lastRunUtc = await runner.GetLastGlobalRunAtAsync(ct);

        // lastRun >= slot → этот слот уже отработан; lastRun свежее 6 дней → debounce
        // (в т.ч. после ручного admin-прогона незадолго до слота).
        if (lastRunUtc is not null
            && (lastRunUtc.Value >= slotUtc || lastRunUtc.Value > nowUtc.AddDays(-MIN_DAYS_BETWEEN_RUNS)))
        {
            _logger.LogDebug(
                "Weekly digest: skip (slot {Slot:u}, last run {LastRun:u})",
                slotUtc, lastRunUtc.Value);
            return;
        }

        int usersNotified = await runner.RunOnceAsync(ct);

        _logger.LogInformation(
            "Weekly digest: slot {Slot:u} processed, {Count} users notified", slotUtc, usersNotified);
    }

    /// <summary>
    /// Ближайшее прошедшее (≤ now) вхождение слота «DayOfWeekUtc в HourUtc:00 UTC».
    /// </summary>
    internal static DateTime MostRecentScheduledSlotUtc(DateTime nowUtc, DayOfWeek dayOfWeek, int hourUtc)
    {
        int hour = Math.Clamp(hourUtc, 0, 23);
        DateTime candidate = new(nowUtc.Year, nowUtc.Month, nowUtc.Day, hour, 0, 0, DateTimeKind.Utc);

        int daysBack = ((int)nowUtc.DayOfWeek - (int)dayOfWeek + 7) % 7;
        candidate = candidate.AddDays(-daysBack);

        if (candidate > nowUtc)
            candidate = candidate.AddDays(-7);

        return candidate;
    }
}
