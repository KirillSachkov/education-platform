using Shared.AI;

namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal static class TimecodeAiSchemas
{
    public static readonly AiJsonSchema TIMECODES = new(
        "timecode_generation_response",
        """
        {
          "type": "object",
          "properties": {
            "language": { "type": "string" },
            "timecodes": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "startSeconds": { "type": "integer" },
                  "endSeconds": { "type": "integer" },
                  "title": { "type": "string" },
                  "confidence": { "type": "number" }
                },
                "required": ["startSeconds", "endSeconds", "title", "confidence"],
                "additionalProperties": false
              }
            }
          },
          "required": ["language", "timecodes"],
          "additionalProperties": false
        }
        """,
        "Final video chapter timecodes with absolute seconds.",
        Strict: true);

    public static readonly AiJsonSchema WINDOW_TOPICS = new(
        "window_topic_proposal_response",
        """
        {
          "type": "object",
          "properties": {
            "language": { "type": "string" },
            "topics": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "startSeconds": { "type": "integer" },
                  "endSeconds": { "type": "integer" },
                  "title": { "type": "string" },
                  "evidence": { "type": "string" },
                  "confidence": { "type": "number" }
                },
                "required": ["startSeconds", "endSeconds", "title", "evidence", "confidence"],
                "additionalProperties": false
              }
            }
          },
          "required": ["language", "topics"],
          "additionalProperties": false
        }
        """,
        "Local topic proposals for a transcript time window.",
        Strict: true);
}
