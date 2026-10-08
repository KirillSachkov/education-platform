using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TrainerService.Core.Configuration;
using TrainerService.Domain;

namespace TrainerService.Core.Features.Shared;

/// <summary>
///     Enforces per-user, per-period AI-usage QUOTAS (#614 C2) via Redis INCR + EXPIRE counters. One
///     counter per (user, dimension, period); the period rolls over by encoding the date in the key
///     (daily <c>yyyyMMdd</c> for OPEN_GRADE, monthly <c>yyyyMM</c> for VOICE/MOCK) and setting an EXPIRE
///     to the period end on the first INCR. Limits are resolved per tier (Free vs Pro by
///     <c>hasPro</c>) from <see cref="TrainerAiLimitsOptions"/>; <b>limit &lt;= 0 ⇒ unlimited</b> on that
///     dimension. Admin uses the Pro tier; it is not exempt from finite limits.
///     <para>
///         <b>Cost is still capped without Redis.</b> On any Redis error the check <b>fails OPEN</b>
///         (logs + allows) — a transient Redis hiccup must never block a paying user, and the spend is
///         still bounded by the audio cap + the PRO gate. This is the OPPOSITE of the entitlement checker
///         (which fails closed) on purpose: a quota is a soft cost-abuse backstop, not a security gate.
///     </para>
/// </summary>
public sealed class TrainerQuotaService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IOptions<TrainerAiOptions> _options;
    private readonly ILogger<TrainerQuotaService> _logger;

    public TrainerQuotaService(
        IConnectionMultiplexer redis,
        IOptions<TrainerAiOptions> options,
        ILogger<TrainerQuotaService> logger)
    {
        _redis = redis;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    ///     Tries to consume one unit on <paramref name="dimension"/> for <paramref name="userId"/>.
    ///     Resolves the limit (Free/Pro by <paramref name="hasPro"/>); limit &lt;= 0 ⇒ unlimited → ok.
    ///     Otherwise INCRs the period counter (EXPIRE set to the period end on the first INCR); if the
    ///     post-INCR value exceeds the limit, decrements back and returns <c>trainer.quota.exceeded</c>
    ///     (403). Fail-open on any Redis error. <b>Лимиты действуют для ВСЕХ, включая админа</b> (#568,
    ///     решение владельца): админ — это Pro-тир (<paramref name="hasPro"/>=true), а не безлимит.
    /// </summary>
    public async Task<UnitResult<Error>> TryConsumeAsync(
        Guid userId,
        QuotaDimension dimension,
        bool hasPro,
        CancellationToken ct)
    {
        // VOICE is metered in audio SECONDS, not a count of answers (#663) — its budget can only be
        // reserved before transcription and finalized once the real duration is known. Route it through
        // TryReserveVoiceAsync + CommitVoiceReservationAsync, never here.
        if (dimension == QuotaDimension.VOICE)
            throw new InvalidOperationException(
                "VOICE quota is metered in audio seconds — use the voice reservation API (#663).");

        TrainerAiTierLimits tier = hasPro ? _options.Value.Limits.Pro : _options.Value.Limits.Free;
        int limit = LimitFor(tier, dimension);
        if (limit <= 0)
            return UnitResult.Success<Error>(); // <= 0 ⇒ unlimited on this dimension.

        DateTimeOffset now = DateTimeOffset.UtcNow;
        (string key, TimeSpan ttl) = KeyAndTtl(userId, dimension, now);

        try
        {
            IDatabase db = _redis.GetDatabase();

            long current = await db.StringIncrementAsync(key);

            // Set the period-end EXPIRE on the FIRST increment of this window (count just became 1).
            if (current == 1)
                await db.KeyExpireAsync(key, ttl);

            if (current > limit)
            {
                // Roll the consumed unit back so a rejected attempt doesn't permanently burn a slot.
                await db.StringDecrementAsync(key);
                return TrainerServiceErrors.Access.QuotaExceeded(dimension, limit);
            }

            return UnitResult.Success<Error>();
        }
#pragma warning disable CA1031 // quota is a soft cost-backstop — a Redis outage must never block the user (fail-open)
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(
                ex,
                "Trainer quota check failed open ({Dimension}, user {UserId}) — Redis unavailable, allowing the call.",
                dimension, userId);
            return UnitResult.Success<Error>();
        }
    }

    /// <summary>
    ///     Atomically reserves the maximum billable length of one voice answer before transcription.
    ///     Reserving first prevents concurrent requests from all passing a read-only quota check and
    ///     overspending the monthly budget. After STT, <see cref="CommitVoiceReservationAsync"/> releases
    ///     the unused part. Unlimited tiers and Redis failures return an inactive reservation (fail-open).
    /// </summary>
    public async Task<Result<VoiceQuotaReservation, Error>> TryReserveVoiceAsync(
        Guid userId,
        bool hasPro,
        int maximumAnswerSeconds,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAnswerSeconds);

        TrainerAiTierLimits tier = hasPro ? _options.Value.Limits.Pro : _options.Value.Limits.Free;
        int limitMinutes = tier.VoiceMinutesPerMonth;
        if (limitMinutes <= 0)
            return VoiceQuotaReservation.None;

        long limitSeconds = (long)limitMinutes * 60;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (string key, TimeSpan ttl) = KeyAndTtl(userId, QuotaDimension.VOICE, now);

        ct.ThrowIfCancellationRequested();
        try
        {
            IDatabase db = _redis.GetDatabase();
            long total = await db.StringIncrementAsync(key, maximumAnswerSeconds);

            if (total == maximumAnswerSeconds)
                await db.KeyExpireAsync(key, ttl);

            if (total > limitSeconds)
            {
                await db.StringDecrementAsync(key, maximumAnswerSeconds);
                return TrainerServiceErrors.Access.QuotaExceeded(QuotaDimension.VOICE, limitMinutes);
            }

            return new VoiceQuotaReservation(key, maximumAnswerSeconds);
        }
#pragma warning disable CA1031 // quota is a soft cost-backstop — a Redis outage must never block the user (fail-open)
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(
                ex,
                "Trainer voice-quota reservation failed open ({Seconds}s, user {UserId}) — allowing the call.",
                maximumAnswerSeconds, userId);
            return VoiceQuotaReservation.None;
        }
    }

    /// <summary>
    ///     Finalizes a prior reservation using the real billable duration. Only the unused portion is
    ///     released; the committed value is clamped to the reserved maximum, so finalization can never
    ///     introduce a second over-budget race.
    /// </summary>
    public async Task CommitVoiceReservationAsync(
        VoiceQuotaReservation reservation,
        int actualSeconds,
        CancellationToken ct)
    {
        if (!reservation.IsActive)
            return;

        int committedSeconds = Math.Clamp(actualSeconds, 0, reservation.ReservedSeconds);
        int releaseSeconds = reservation.ReservedSeconds - committedSeconds;
        if (releaseSeconds == 0)
            return;

        ct.ThrowIfCancellationRequested();
        try
        {
            await _redis.GetDatabase().StringDecrementAsync(reservation.RedisKey!, releaseSeconds);
        }
#pragma warning disable CA1031 // metering cleanup is best-effort and must never fail the user request
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(
                ex,
                "Trainer voice-quota finalization failed ({ReleaseSeconds}s release) — reservation retained.",
                releaseSeconds);
        }
    }

    public Task ReleaseVoiceReservationAsync(VoiceQuotaReservation reservation) =>
        CommitVoiceReservationAsync(reservation, actualSeconds: 0, CancellationToken.None);

    /// <summary>
    ///     Снимок остатка AI-лимитов вызывающего (#568) — для карточки «Trainer Pro / лимиты». Читает
    ///     текущие счётчики БЕЗ инкремента; <c>limit &lt;= 0</c> ⇒ безлимит (<c>Limit=null</c>). Лимиты
    ///     действуют для всех, включая админа (он — Pro-тир по <paramref name="hasPro"/>).
    ///     Fail-open: ошибка Redis → <c>Used=0</c> (показ не блокируем).
    /// </summary>
    public async Task<IReadOnlyList<TrainerQuotaUsage>> GetUsageSnapshotAsync(
        Guid userId,
        bool hasPro,
        CancellationToken ct)
    {
        TrainerAiTierLimits tier = hasPro ? _options.Value.Limits.Pro : _options.Value.Limits.Free;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        QuotaDimension[] dimensions = [QuotaDimension.OPEN_GRADE, QuotaDimension.VOICE, QuotaDimension.MOCK];

        var result = new List<TrainerQuotaUsage>(dimensions.Length);
        foreach (QuotaDimension dimension in dimensions)
        {
            int limit = LimitFor(tier, dimension);
            if (limit <= 0)
            {
                result.Add(new TrainerQuotaUsage(dimension, Limit: null, Used: 0));
                continue;
            }

            long usedRaw = await ReadCountAsync(userId, dimension, now);
            // VOICE is metered in audio MINUTES (the Redis counter stores seconds) — surface used minutes,
            // rounded up, capped at the limit (#663). OPEN_GRADE / MOCK stay raw counts.
            int used = dimension == QuotaDimension.VOICE
                ? (int)Math.Min((usedRaw + 59) / 60, limit)
                : (int)Math.Min(usedRaw, limit);
            result.Add(new TrainerQuotaUsage(dimension, limit, used));
        }

        return result;
    }

    /// <summary>Текущее значение счётчика квоты без инкремента. Ошибка Redis / отсутствие ключа → 0.</summary>
    private async Task<long> ReadCountAsync(Guid userId, QuotaDimension dimension, DateTimeOffset now)
    {
        try
        {
            (string key, _) = KeyAndTtl(userId, dimension, now);
            RedisValue value = await _redis.GetDatabase().StringGetAsync(key);
            return value.TryParse(out long count) ? count : 0;
        }
#pragma warning disable CA1031 // peek is best-effort — a Redis hiccup shows 0 used, never blocks the page
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "Trainer quota peek failed ({Dimension}, user {UserId}).", dimension, userId);
            return 0;
        }
    }

    private static int LimitFor(TrainerAiTierLimits tier, QuotaDimension dimension) => dimension switch
    {
        QuotaDimension.OPEN_GRADE => tier.OpenGradesPerDay,
        QuotaDimension.VOICE => tier.VoiceMinutesPerMonth,
        QuotaDimension.MOCK => tier.MockPerMonth,
        _ => 0,
    };

    /// <summary>
    ///     Builds the counter key and its TTL. OPEN_GRADE is daily (<c>yyyyMMdd</c>, TTL to next UTC
    ///     midnight); VOICE/MOCK are monthly (<c>yyyyMM</c>, TTL to the first UTC day of next month).
    /// </summary>
    private static (string Key, TimeSpan Ttl) KeyAndTtl(Guid userId, QuotaDimension dimension, DateTimeOffset now)
    {
        if (dimension == QuotaDimension.OPEN_GRADE)
        {
            string day = now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            DateTimeOffset nextMidnight = new DateTimeOffset(now.Date, TimeSpan.Zero).AddDays(1);
            return ($"trainer:quota:{userId}:open_grade:{day}", nextMidnight - now);
        }

        string segment = dimension == QuotaDimension.VOICE ? "voice" : "mock";
        string month = now.ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture);
        DateTimeOffset firstOfNextMonth =
            new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        return ($"trainer:quota:{userId}:{segment}:{month}", firstOfNextMonth - now);
    }
}

/// <summary>Остаток лимита по одному измерению AI-использования (#568). <c>Limit=null</c> ⇒ безлимит.</summary>
public sealed record TrainerQuotaUsage(QuotaDimension Dimension, int? Limit, int Used);

public sealed record VoiceQuotaReservation(string? RedisKey, int ReservedSeconds)
{
    public static VoiceQuotaReservation None { get; } = new(null, 0);

    public bool IsActive => RedisKey is not null && ReservedSeconds > 0;
}
