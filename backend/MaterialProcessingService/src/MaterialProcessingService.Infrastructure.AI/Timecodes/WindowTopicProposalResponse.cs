namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal sealed record WindowTopicProposalResponse(
    string Language,
    List<WindowTopicProposalItemResponse> Topics);
