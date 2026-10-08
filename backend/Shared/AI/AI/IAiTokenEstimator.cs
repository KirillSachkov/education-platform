namespace Shared.AI;

public interface IAiTokenEstimator
{
    AiTokenEstimate Estimate(AiTokenEstimateRequest request);
}
