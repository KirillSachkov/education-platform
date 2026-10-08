namespace MaterialProcessingService.Infrastructure.AI.ContentDrafts;

internal sealed class ContentChunkResponse
{
    public string Language { get; set; } = string.Empty;

    public string ContentMarkdown { get; set; } = string.Empty;
}
