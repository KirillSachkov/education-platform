namespace Shared.AI;

public sealed record AiModelInfo(
    string Model,
    int ContextWindowTokens,
    bool SupportsAudioInput,
    bool SupportsJsonResponseFormat,
    bool SupportsJsonSchemaResponseFormat,
    bool SupportsSystemMessage,
    bool IsMetadataAvailable);
