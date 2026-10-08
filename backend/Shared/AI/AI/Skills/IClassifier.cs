using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI.Skills;

/// <summary>
///     «Текст → одна из меток + confidence» через JSON Schema-выход. Используется для
///     мягкой классификации (тип материала, категория feedback'а, severity тикета и т.п.).
/// </summary>
public interface IClassifier<TLabel> where TLabel : struct, Enum
{
    Task<Result<AiClassification<TLabel>, Error>> ClassifyAsync(
        ClassifyRequest<TLabel> request,
        CancellationToken cancellationToken = default);
}

public sealed record ClassifyRequest<TLabel> where TLabel : struct, Enum
{
    public required string Model { get; init; }

    public required string Text { get; init; }

    /// <summary>
    ///     Описание задачи в свободной форме («тон сообщения», «тематика статьи»).
    ///     Подмешивается в system prompt.
    /// </summary>
    public required string TaskDescription { get; init; }

    /// <summary>Подсказки по каждой метке: словарь label→описание.</summary>
    public IReadOnlyDictionary<TLabel, string>? LabelDescriptions { get; init; }

    public int? TimeoutSeconds { get; init; }
}

public sealed record AiClassification<TLabel>(
    TLabel Label,
    double Confidence,
    string? Rationale,
    AiUsage? Usage,
    AiFinishReason FinishReason) where TLabel : struct, Enum;
