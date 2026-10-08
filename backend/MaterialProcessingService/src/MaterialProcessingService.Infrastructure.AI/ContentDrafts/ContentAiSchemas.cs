using Shared.AI;

namespace MaterialProcessingService.Infrastructure.AI.ContentDrafts;

internal static class ContentAiSchemas
{
    public static readonly AiJsonSchema VIDEO_CONTENT = new(
        "video_content",
        """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "language": { "type": "string" },
                "contentMarkdown": { "type": "string" }
              },
              "required": ["language", "contentMarkdown"]
            }
            """,
        Strict: true);

    public static readonly AiJsonSchema CONTENT_CHUNK = new(
        "video_content_chunk",
        """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "language": { "type": "string" },
                "contentMarkdown": { "type": "string" }
              },
              "required": ["language", "contentMarkdown"]
            }
            """,
        Strict: true);
}
