namespace Shared.AI;

public sealed record AiGenerationResult<T>(
    T Value,
    string Provider,
    string Model,
    AiUsage? Usage,
    AiFinishReason FinishReason);

public sealed record AiUsage(
    int InputTokens,
    int OutputTokens,
    int TotalTokens);

public enum AiFinishReason
{
    Unknown = 0,
    Stop = 1,
    Length = 2,
    ContentFilter = 3,
    ToolCalls = 4,
}
