namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal sealed record WindowTopicProposal(
    int StartSeconds,
    int? EndSeconds,
    string Title,
    string Evidence,
    double Confidence);
