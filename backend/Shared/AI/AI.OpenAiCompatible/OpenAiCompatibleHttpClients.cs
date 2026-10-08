namespace Shared.AI.OpenAiCompatible;

/// <summary>
///     Имена named HttpClient'ов, регистрируемых OpenAiCompatible adapter'ом.
///     Sharing HttpClient pool между всеми провайдерами — HttpClientFactory
///     управляет handler-pooling, а per-request timeout и Authorization
///     ставятся per-call (см. <see cref="OpenAiCompatibleTranscriptionClient"/>,
///     <see cref="OpenAiCompatibleModelMetadataClient"/>).
/// </summary>
internal static class OpenAiCompatibleHttpClients
{
    public const string CHAT = "openai-compatible-chat";
    public const string TRANSCRIPTION = "openai-compatible-transcription";
    public const string EMBEDDINGS = "openai-compatible-embeddings";
}
