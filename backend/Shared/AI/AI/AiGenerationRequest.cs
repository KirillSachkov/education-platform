namespace Shared.AI;

public sealed class AiGenerationRequest
{
    public required string Model { get; init; }

    public required string SystemPrompt { get; init; }

    public string? UserPrompt { get; init; }

    public object? Input { get; init; }

    public IReadOnlyList<AiInputPart> InputParts { get; init; } = [];

    public double? Temperature { get; init; }

    public int? MaxOutputTokens { get; init; }

    public int? TimeoutSeconds { get; init; }

    public AiOutputMode? OutputMode { get; init; }

    public AiJsonSchema? JsonSchema { get; init; }
}
