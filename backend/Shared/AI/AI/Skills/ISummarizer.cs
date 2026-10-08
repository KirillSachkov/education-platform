using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI.Skills;

/// <summary>
///     Reusable «text → конспект» helper. Прямая замена тому, что в каждом сервисе раньше
///     писали через копи-пасту IAiClient + ad-hoc system prompt.
/// </summary>
public interface ISummarizer
{
    Task<Result<AiSummary, Error>> SummarizeAsync(
        SummarizeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record SummarizeRequest
{
    public required string Model { get; init; }

    /// <summary>Исходный текст. Обязательное поле.</summary>
    public required string Text { get; init; }

    /// <summary>Стиль конспекта.</summary>
    public SummaryStyle Style { get; init; } = SummaryStyle.Bullets;

    /// <summary>Желаемая длина (примерно). Не jhh-hard cap, prompt-hint.</summary>
    public SummaryLength Length { get; init; } = SummaryLength.Medium;

    /// <summary>Язык вывода. Пустой → язык исходника.</summary>
    public string? Language { get; init; }

    /// <summary>
    ///     Дополнительный контекст для prompt (например «целевая аудитория — .NET разработчики»).
    ///     Прокидывается в system prompt как guidance.
    /// </summary>
    public string? Context { get; init; }

    public double? Temperature { get; init; }

    public int? MaxOutputTokens { get; init; }

    public int? TimeoutSeconds { get; init; }
}

public enum SummaryStyle
{
    /// <summary>Список ключевых тезисов.</summary>
    Bullets,

    /// <summary>Связный текст в 1–3 абзацах.</summary>
    Prose,

    /// <summary>Структура с подзаголовками.</summary>
    Sections,
}

public enum SummaryLength
{
    Brief,
    Medium,
    Detailed,
}

public sealed record AiSummary(
    string Text,
    AiUsage? Usage,
    AiFinishReason FinishReason);
