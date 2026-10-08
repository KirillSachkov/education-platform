namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal sealed record CompactTranscriptBlock(
    TimeSpan Start,
    TimeSpan End,
    string Text);
