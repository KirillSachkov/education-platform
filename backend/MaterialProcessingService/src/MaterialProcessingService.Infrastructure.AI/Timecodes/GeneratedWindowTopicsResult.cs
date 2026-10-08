namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal sealed record GeneratedWindowTopicsResult(
    string Language,
    IReadOnlyList<WindowTopicProposal> Topics);
