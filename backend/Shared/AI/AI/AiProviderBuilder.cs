using Microsoft.Extensions.DependencyInjection;

namespace Shared.AI;

/// <summary>
///     Фабричный делегат: получает на вход <see cref="IServiceProvider"/> и
///     per-provider'ный <see cref="AiOptions"/>, возвращает построенный
///     <see cref="IAiClient"/> уже без MeteredAiClient-обёртки (декорирование
///     делает <see cref="AiClientFactory"/> снаружи).
/// </summary>
public delegate IAiClient AiClientConstructor(IServiceProvider services, AiOptions options);

/// <summary>
///     Опциональный фабричный делегат для STT — провайдер может не поддерживать
///     транскрипцию, тогда регистрация просто опускает этот делегат.
/// </summary>
public delegate IAiTranscriptionClient AiTranscriptionClientConstructor(IServiceProvider services, AiOptions options);

/// <summary>
///     Опциональный фабричный делегат для embeddings — провайдер может не
///     поддерживать <c>/v1/embeddings</c> endpoint, тогда регистрация просто
///     опускает этот делегат. Используется AssignmentReviewService (#15) для
///     RAG-индексации код-чанков.
/// </summary>
public delegate IAiEmbeddingsClient AiEmbeddingsClientConstructor(IServiceProvider services, AiOptions options);

/// <summary>
///     Регистрация адаптера AI-провайдера. Адаптер (OpenAiCompatible, в будущем,
///     возможно, нативный Anthropic SDK / Gemini SDK) подключается через
///     <c>builder.AddProvider(kind, chatCtor, transcriptionCtor, embeddingsCtor)</c>.
///     На run-time <see cref="AiClientFactory"/> ищет адаптер по <c>kind</c>
///     и зовёт ctor с per-provider <see cref="AiOptions"/>.
/// </summary>
public sealed class AiProviderBuilder
{
    private readonly Dictionary<string, AiProviderAdapter> _adapters =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<Action<IServiceCollection>> _sharedRegistrations = [];

    /// <summary>
    ///     Зарегистрировать адаптер. Один и тот же <paramref name="kind"/> можно
    ///     зарегистрировать несколько раз — последний победит (используется для
    ///     legacy alias'ов, например, <c>RouterAI</c> → <c>OpenAiCompatible</c>).
    /// </summary>
    /// <param name="kind">Идентификатор в config'е (<c>AI:Providers:{name}:Kind</c>).</param>
    /// <param name="chatConstructor">Builder для chat-клиента.</param>
    /// <param name="transcriptionConstructor">Builder для STT-клиента (null если не поддерживается).</param>
    /// <param name="embeddingsConstructor">Builder для embeddings-клиента (null если не поддерживается).</param>
    /// <param name="registerSharedServices">
    ///     Hook для регистрации однократных shared-сервисов в DI (HttpClient'ов с
    ///     resilience pipeline'ом, например). Вызывается один раз при регистрации
    ///     адаптера; per-provider state живёт внутри клиентов, не в DI.
    /// </param>
    public AiProviderBuilder AddProvider(
        string kind,
        AiClientConstructor chatConstructor,
        AiTranscriptionClientConstructor? transcriptionConstructor = null,
        AiEmbeddingsClientConstructor? embeddingsConstructor = null,
        Action<IServiceCollection>? registerSharedServices = null)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("AI provider kind is required", nameof(kind));

        _adapters[kind.Trim()] = new AiProviderAdapter(
            chatConstructor,
            transcriptionConstructor,
            embeddingsConstructor);

        if (registerSharedServices is not null)
            _sharedRegistrations.Add(registerSharedServices);

        return this;
    }

    internal IReadOnlyDictionary<string, AiProviderAdapter> Adapters => _adapters;

    internal void ApplySharedRegistrations(IServiceCollection services)
    {
        foreach (Action<IServiceCollection> action in _sharedRegistrations)
            action(services);
    }
}

internal sealed record AiProviderAdapter(
    AiClientConstructor ChatConstructor,
    AiTranscriptionClientConstructor? TranscriptionConstructor,
    AiEmbeddingsClientConstructor? EmbeddingsConstructor);
