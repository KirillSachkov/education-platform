namespace Shared.AI;

/// <summary>
///     Резолвер per-provider'ного <see cref="IAiClient"/>. Multi-provider
///     абстракция: разные сервисы (или slot'ы внутри сервиса) могут указать
///     разных провайдеров. <see cref="Get"/> возвращает <see cref="IAiClient"/>
///     уже обёрнутый в <c>MeteredAiClient</c> (cost/usage observability).
/// </summary>
public interface IAiClientFactory
{
    /// <summary>
    ///     Возвращает клиента для указанного провайдера.
    ///     Если <paramref name="providerName"/> пустой/null — возвращает default-провайдера.
    /// </summary>
    /// <exception cref="InvalidOperationException">Провайдер с таким именем не зарегистрирован.</exception>
    IAiClient Get(string? providerName);

    /// <summary>Имя default-провайдера (для логирования/диагностики).</summary>
    string DefaultProviderName { get; }
}

/// <summary>
///     Резолвер per-provider'ного <see cref="IAiTranscriptionClient"/>.
///     Не все провайдеры поддерживают STT (RouterAI, например) — для таких
///     <see cref="Get"/> бросает <see cref="InvalidOperationException"/>.
/// </summary>
public interface IAiTranscriptionClientFactory
{
    /// <summary>
    ///     Возвращает STT-клиента для указанного провайдера.
    ///     Если <paramref name="providerName"/> пустой/null — возвращает default-провайдера.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Провайдер не зарегистрирован, или провайдер не поддерживает STT.
    /// </exception>
    IAiTranscriptionClient Get(string? providerName);

    string DefaultProviderName { get; }
}

/// <summary>
///     Резолвер per-provider'ного <see cref="IAiEmbeddingsClient"/>.
///     Не все провайдеры поддерживают embeddings — для таких <see cref="Get"/>
///     бросает <see cref="InvalidOperationException"/>. Используется
///     AssignmentReviewService (#15) для RAG-индексации код-чанков.
/// </summary>
public interface IAiEmbeddingsClientFactory
{
    /// <summary>
    ///     Возвращает embeddings-клиента для указанного провайдера.
    ///     Если <paramref name="providerName"/> пустой/null — возвращает default-провайдера.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Провайдер не зарегистрирован, или провайдер не поддерживает embeddings.
    /// </exception>
    IAiEmbeddingsClient Get(string? providerName);

    string DefaultProviderName { get; }
}
