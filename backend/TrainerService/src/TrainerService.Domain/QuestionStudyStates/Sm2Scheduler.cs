namespace TrainerService.Domain.QuestionStudyStates;

/// <summary>
/// Pure, deterministic SM-2-lite spaced-repetition scheduler (Anki-style, #568 Ф2).
/// Given the previous SRS state + a success/failure signal + <c>now</c>, computes the next
/// schedule. No DB, no clock-of-its-own — fully testable. Used by <see cref="QuestionStudyState"/>.
/// </summary>
/// <remarks>
/// Classic SM-2 with a binary quality mapping (knew → 4, failed → 1 on the 0..5 scale):
/// <list type="bullet">
///   <item>success: repetitions++; interval = reps==1 ? 1 : reps==2 ? 6 : round(prevInterval * EF);</item>
///   <item>failure: repetitions=0, interval=1 (relearn tomorrow);</item>
///   <item>EF' = EF + (0.1 - (5-q)*(0.08 + (5-q)*0.02)), clamped to a floor of 1.3.</item>
/// </list>
/// q=4 leaves EF unchanged (delta 0) so a clean streak yields 1d → 6d → 6d*EF…; repeated
/// failures (q=1, delta −0.54) drive EF down to the 1.3 floor.
/// </remarks>
public static class Sm2Scheduler
{
    public const double MinEaseFactor = 1.3;
    public const double DefaultEaseFactor = 2.5;

    private const int SuccessQuality = 4;
    private const int FailureQuality = 1;

    /// <summary>Immutable snapshot of the scheduling state SM-2 reads and rewrites.</summary>
    public readonly record struct Sm2State(double EaseFactor, int IntervalDays, int Repetitions);

    public static Sm2State Schedule(Sm2State previous, bool success, DateTimeOffset now, out DateTimeOffset nextDueAt)
    {
        int quality = success ? SuccessQuality : FailureQuality;
        double ease = AdjustEaseFactor(previous.EaseFactor, quality);

        int repetitions;
        int intervalDays;

        if (!success)
        {
            // Lapse: relearn from scratch, due tomorrow.
            repetitions = 0;
            intervalDays = 1;
        }
        else
        {
            repetitions = previous.Repetitions + 1;
            intervalDays = repetitions switch
            {
                1 => 1,
                2 => 6,
                _ => (int)Math.Round(previous.IntervalDays * ease, MidpointRounding.AwayFromZero),
            };

            // Guard: never schedule sooner than the next day on a success.
            if (intervalDays < 1)
                intervalDays = 1;
        }

        nextDueAt = now.AddDays(intervalDays);
        return new Sm2State(ease, intervalDays, repetitions);
    }

    private static double AdjustEaseFactor(double ease, int quality)
    {
        double delta = 0.1 - ((5 - quality) * (0.08 + ((5 - quality) * 0.02)));
        double adjusted = ease + delta;
        return adjusted < MinEaseFactor ? MinEaseFactor : adjusted;
    }
}
