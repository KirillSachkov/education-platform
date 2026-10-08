namespace Shared.AI.OpenAiCompatible;

internal sealed record OpenAiCompatibleResolvedRequest(
    string Model,
    double? Temperature,
    int MaxOutputTokens,
    int TimeoutSeconds,
    AiOutputMode OutputMode,
    AiJsonSchema? JsonSchema,
    AiModelInfo ModelInfo,
    AiBudgetAnalysis Analysis);
