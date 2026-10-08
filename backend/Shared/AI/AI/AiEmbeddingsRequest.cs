namespace Shared.AI;

/// <summary>
///     Запрос на расчёт embedding-векторов. Каждый элемент <see cref="Inputs"/>
///     embedding'уется отдельно — по индексу позиции возвращается соответствующий
///     <see cref="AiEmbedding"/> в <see cref="AiEmbeddingsResult.Embeddings"/>.
///
///     Hard-limit на количество элементов в batch'е — за провайдером (OpenAI hard-cap
///     2048; в нашем клиенте — 64 для consistent latency). Если нужно больше — caller
///     батчит сам.
/// </summary>
public sealed record AiEmbeddingsRequest
{
    public required string Model { get; init; }

    public required IReadOnlyList<string> Inputs { get; init; }

    /// <summary>
    ///     Per-request override. Без него используется HttpClient.Timeout, заданный
    ///     при регистрации (default 60s).
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}
