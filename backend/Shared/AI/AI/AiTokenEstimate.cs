namespace Shared.AI;

public sealed record AiTokenEstimate(
    int TokenCount,
    AiTokenEstimateConfidence Confidence);

public enum AiTokenEstimateConfidence
{
    Low = 1,
    Medium = 2,
    High = 3,
}
