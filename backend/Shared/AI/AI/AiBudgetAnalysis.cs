namespace Shared.AI;

public sealed record AiBudgetAnalysis(
    string Provider,
    string Model,
    int ContextWindowTokens,
    int EstimatedInputTokens,
    int ReservedOutputTokens,
    bool Fits,
    AiTokenEstimateConfidence EstimateConfidence)
{
    public int OverflowTokens => Math.Max(0, EstimatedInputTokens + ReservedOutputTokens - ContextWindowTokens);
}
