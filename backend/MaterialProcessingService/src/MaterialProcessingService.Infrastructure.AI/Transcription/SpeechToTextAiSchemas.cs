using Shared.AI;

namespace MaterialProcessingService.Infrastructure.AI.Transcription;

internal static class SpeechToTextAiSchemas
{
    public static readonly AiJsonSchema RESPONSE = new(
        "speech_to_text_response",
        """
        {
          "type": "object",
          "properties": {
            "language": { "type": "string" },
            "segments": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "startSeconds": { "type": "number" },
                  "endSeconds": { "type": "number" },
                  "text": { "type": "string" }
                },
                "required": ["startSeconds", "endSeconds", "text"],
                "additionalProperties": false
              }
            }
          },
          "required": ["language", "segments"],
          "additionalProperties": false
        }
        """,
        "Speech transcription split into timestamped segments.",
        Strict: true);
}
