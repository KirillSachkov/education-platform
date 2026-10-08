namespace Shared.AI;

public sealed record AiTokenEstimateRequest(
    AiGenerationRequest Request,
    AiTokenEstimateConfidence Confidence);
