using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI.Skills;

/// <summary>
///     «Текст → типизированная структура» через JSON Schema. Тонкая обёртка над
///     <see cref="IAiClient.GenerateAsync{T}"/> с тем же API surface, что у Skill'ов
///     (одинаковая регистрация в DI, легко заменить на сервис-специфичный extractor).
/// </summary>
public interface IStructuredExtractor<TPayload>
{
    Task<Result<AiStructured<TPayload>, Error>> ExtractAsync(
        StructuredExtractRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record StructuredExtractRequest
{
    public required string Model { get; init; }

    public required string SystemPrompt { get; init; }

    public required string UserText { get; init; }

    public required AiJsonSchema Schema { get; init; }

    public double? Temperature { get; init; }

    public int? MaxOutputTokens { get; init; }

    public int? TimeoutSeconds { get; init; }
}

public sealed record AiStructured<TPayload>(
    TPayload Payload,
    AiUsage? Usage,
    AiFinishReason FinishReason);
