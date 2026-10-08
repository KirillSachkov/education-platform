using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI;

/// <summary>
///     Provider-agnostic embeddings client. Возвращает по одному вектору на каждый
///     элемент <see cref="AiEmbeddingsRequest.Inputs"/>. Зеркалит OpenAI
///     <c>POST /v1/embeddings</c> shape — большинство OpenAI-совместимых провайдеров
///     (Polza, OpenRouter, ProxyAPI) поддерживают тот же endpoint.
///
///     Использование: RAG-индексация (chunk → embedding → vector store) и retrieval
///     (query → embedding → top-K cosine).
/// </summary>
public interface IAiEmbeddingsClient
{
    Task<Result<AiEmbeddingsResult, Error>> EmbedAsync(
        AiEmbeddingsRequest request,
        CancellationToken cancellationToken = default);
}
