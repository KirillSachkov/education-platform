namespace MaterialProcessingService.Infrastructure.AI.ContentDrafts;

internal sealed record GeneratedContentChunk(
    int StartSeconds,
    int EndSeconds,
    string ContentMarkdown);
