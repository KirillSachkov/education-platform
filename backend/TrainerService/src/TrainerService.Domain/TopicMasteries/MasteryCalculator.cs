namespace TrainerService.Domain.TopicMasteries;

/// <summary>
///     The latest scored attempt on ONE distinct question, used to derive topic mastery (#691).
///     <paramref name="ScorePercent"/> is the score of the most-recent answered attempt for that
///     question; <paramref name="Difficulty"/> is its snapshot difficulty literal (JUNIOR/MIDDLE/
///     SENIOR or <c>null</c> when unspecified) and drives the weight.
/// </summary>
public readonly record struct QuestionLatestScore(Guid QuestionId, int ScorePercent, string? Difficulty);

/// <summary>
///     Derives topic mastery as the difficulty-weighted average of the LATEST score per UNIQUE
///     question the user answered in the topic (#691). This kills farming — re-answering the same
///     question only ever refreshes that one question's latest score, never adds a new term — and
///     weights harder questions more (a SENIOR answer moves mastery more than a JUNIOR one).
///     <para>
///         Weights are tunable consts: JUNIOR 0.7 / MIDDLE 1.0 / SENIOR 1.3 / unspecified 1.0.
///     </para>
/// </summary>
public static class MasteryCalculator
{
    public const double WEIGHT_JUNIOR = 0.7;
    public const double WEIGHT_MIDDLE = 1.0;
    public const double WEIGHT_SENIOR = 1.3;
    public const double WEIGHT_DEFAULT = 1.0;

    /// <summary>Difficulty weight for a snapshot difficulty literal; unknown/null → <see cref="WEIGHT_DEFAULT"/>.</summary>
    public static double WeightFor(string? difficulty) => difficulty switch
    {
        "JUNIOR" => WEIGHT_JUNIOR,
        "MIDDLE" => WEIGHT_MIDDLE,
        "SENIOR" => WEIGHT_SENIOR,
        _ => WEIGHT_DEFAULT,
    };

    /// <summary>
    ///     Computes (mastery%, answersCount) from the distinct-question latest scores. The input MUST
    ///     already be deduplicated to at most one entry per question (the latest scored attempt) — the
    ///     answers count is just the number of distinct questions. Empty input → (0, 0).
    /// </summary>
    public static (int MasteryPercent, int AnswersCount) Compute(
        IReadOnlyCollection<QuestionLatestScore> distinctLatest)
    {
        if (distinctLatest.Count == 0)
            return (0, 0);

        double weightedSum = 0;
        double weightTotal = 0;
        foreach (QuestionLatestScore q in distinctLatest)
        {
            double weight = WeightFor(q.Difficulty);
            weightedSum += weight * q.ScorePercent;
            weightTotal += weight;
        }

        int mastery = weightTotal <= 0
            ? 0
            : (int)Math.Round(weightedSum / weightTotal, MidpointRounding.AwayFromZero);

        return (Math.Clamp(mastery, 0, 100), distinctLatest.Count);
    }
}
