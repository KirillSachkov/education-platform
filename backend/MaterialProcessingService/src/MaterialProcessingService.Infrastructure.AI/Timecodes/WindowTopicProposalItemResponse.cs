namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal sealed record WindowTopicProposalItemResponse(
    int StartSeconds,
    int? EndSeconds,
    string Title,
    string Evidence,
    double Confidence);
