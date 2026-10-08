namespace Shared.AI;

/// <summary>
///     Ответ от embedding-провайдера. Порядок <see cref="Embeddings"/> может отличаться
///     от порядка <see cref="AiEmbeddingsRequest.Inputs"/> (OpenAI гарантирует, что
///     каждый item имеет <see cref="AiEmbedding.Index"/> — caller сам сортирует, если
///     нужно).
/// </summary>
public sealed record AiEmbeddingsResult
{
    public required IReadOnlyList<AiEmbedding> Embeddings { get; init; }

    /// <summary>
    ///     Сколько токенов было биллинговано провайдером для всего batch'а. Используется
    ///     для cost-аналитики через <c>EducationPlatform.AI</c> Meter.
    /// </summary>
    public required int InputTokens { get; init; }

    /// <summary>
    ///     Имя модели как вернул провайдер. OpenAI отдаёт canonical name
    ///     (<c>"text-embedding-3-small"</c>); Polza чаще всего отдаёт исходный alias
    ///     с префиксом провайдера (<c>"openai/text-embedding-3-small"</c>). Caller
    ///     при необходимости приводит формат сам — клиент не нормализует.
    ///     Если провайдер не вернул поле <c>model</c>, используется значение из
    ///     <see cref="AiEmbeddingsRequest.Model"/> (input → output passthrough).
    /// </summary>
    public required string ModelUsed { get; init; }
}

public sealed record AiEmbedding
{
    public required int Index { get; init; }

    public required IReadOnlyList<float> Vector { get; init; }
}
