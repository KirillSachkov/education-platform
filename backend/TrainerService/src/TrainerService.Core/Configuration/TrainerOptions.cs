namespace TrainerService.Core.Configuration;

/// <summary>
///     General trainer monetization knobs (#674), bound from the <c>Trainer</c> config section.
///     Distinct from <see cref="TrainerAiOptions"/> (the <c>TrainerAI</c> section), which holds AI
///     pricing / quotas / models.
/// </summary>
public sealed class TrainerOptions
{
    public const string SECTION_NAME = "Trainer";

    /// <summary>Default free-sample share per difficulty bucket, %. Owner-tunable in config.</summary>
    public const int DEFAULT_FREE_SAMPLE_PERCENT = 10;

    /// <summary>
    ///     Share (%) of auto-gradable questions per difficulty bucket exposed free to non-PRO users as
    ///     a taste (#674). Read by <c>TrainerFreeAllocationPolicy</c>. Each bucket always yields at
    ///     least one free question (the policy floors the count at 1).
    /// </summary>
    public int FreeSamplePercent { get; set; } = DEFAULT_FREE_SAMPLE_PERCENT;
}
