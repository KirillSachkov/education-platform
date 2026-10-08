using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Shared.AI.OpenAiCompatible;

/// <summary>
///     OpenAI-compatible transcription через POST <c>/v1/audio/transcriptions</c>.
///     Используется для STT-моделей <c>gpt-4o-transcribe</c>,
///     <c>gpt-4o-mini-transcribe</c>, <c>whisper-1</c>. В отличие от chat-API
///     с audio input (<see cref="OpenAiCompatibleClient"/>), этот endpoint:
///     <list type="bullet">
///         <item>билингуется в минутах аудио, не audio-tokens (предсказуемая цена);</item>
///         <item>возвращает нативные timestamps на уровне сегментов;</item>
///         <item>support для языковых hint'ов (<c>language=ru</c>) — снижает
///             hallucination на технической лексике.</item>
///     </list>
///     Endpoint resolved через <c>BaseUrl</c> из <see cref="AiOptions"/> — работает
///     с любым OpenAI-compat прокси, который поддерживает Whisper-style endpoint
///     (Polza, AITunnel, OpenAI direct, ProxyAPI). RouterAI отсутствует — у них
///     нет dedicated transcription endpoint.
/// </summary>
public sealed class OpenAiCompatibleTranscriptionClient : IAiTranscriptionClient
{
    private const int DEFAULT_TIMEOUT_SECONDS = 900;
    // BaseUrl уже содержит `/v1/` суффикс (например `https://api.polza.ai/api/v1/`).
    // Sibling-клиенты (ModelMetadata=`models`, Chat — endpoint=BaseUrl) тоже без `v1/`.
    // Двойной `/v1/v1/audio/transcriptions` вёл к 404 от Polza.
    private const string ENDPOINT_PATH = "audio/transcriptions";
    private const string PROVIDER_NAME = "OpenAiCompatible";

    // Polza /transcriptions hard-limit (задокументирован на /api-reference/audio/transcriptions).
    // OpenAI Whisper тоже 25 МБ — единый whisper-style стандарт. AITunnel тоже подтвердил
    // 25 MB limit в docs/api/limits.html. Чанки >25 МБ режектятся upstream'ом,
    // иногда с 503 вместо 413 — лучше упасть рано с понятной ошибкой.
    private const int MAX_AUDIO_BYTES = 25 * 1024 * 1024;

    // Retry на 5xx / transient HttpRequestException делается на уровне
    // HttpClient pipeline через Polly (см. DependencyInjectionExtensions.AddProviderResilience).
    // Здесь — простой single-shot HTTP вызов; retry прозрачен для этого кода.

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiCompatibleTranscriptionClient> _logger;

    public OpenAiCompatibleTranscriptionClient(
        AiOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAiCompatibleTranscriptionClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<Result<AiTranscriptionResult, Error>> TranscribeAsync(
        AiTranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return AiErrors.ProviderUnauthorized();

        if (request.AudioBytes.Count == 0)
            return AiErrors.InputRequired();

        if (request.AudioBytes.Count > MAX_AUDIO_BYTES)
        {
            _logger.LogWarning(
                "Transcription chunk too large: {Bytes} B exceeds limit {Limit} B for model {Model}",
                request.AudioBytes.Count,
                MAX_AUDIO_BYTES,
                request.Model);

            return Error.Validation(
                "ai.transcription.audio.too_large",
                "Аудио-чанк превышает лимит 25 МБ для STT API. Уменьшите ChunkSeconds или bit-rate.");
        }

        int timeoutSeconds = Math.Max(1, request.TimeoutSeconds ?? _options.TimeoutSeconds ?? DEFAULT_TIMEOUT_SECONDS);
        Uri endpoint = new(NormalizeBaseUrl(_options.BaseUrl), ENDPOINT_PATH);

        byte[] audioBytes = request.AudioBytes is byte[] arr ? arr : request.AudioBytes.ToArray();

        // Multipart/form-data — стандартный OpenAI-style flow для /audio/transcriptions.
        // Polza, AITunnel, OpenAI direct — все принимают multipart с теми же полями.
        // Раньше клиент гонял JSON+base64 (data-URI) — payload раздувался на 33% и
        // нагружал sync-сериализацию. ByteArrayContent multi-read safe — Polly retry
        // может переотправить тот же content без re-construction.
        //
        // Provider routing (Polza-specific): allow_fallbacks=true + sort=throughput.
        // Включается ТОЛЬКО если AiOptions.SendProviderRoutingHints=true. Другие
        // провайдеры (AITunnel и т.д.) либо игнорируют, либо могут 400'нуть.
        //
        // chunking_strategy сознательно НЕ передаём — он required только для
        // gpt-4o-transcribe-diarize. Лишний параметр на части upstream'ов даёт 400/503.
        using var content = new MultipartFormDataContent();

        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(NormalizeContentType(request.ContentType));
        content.Add(audioContent, "file", string.IsNullOrWhiteSpace(request.FileName) ? "audio.mp3" : request.FileName);

        content.Add(new StringContent(request.Model), "model");

        // OpenAI: `whisper-1` поддерживает verbose_json + native segment timestamps.
        // `gpt-4o-(mini-)transcribe` — только json/text (AITunnel rejects 400 на
        // verbose_json, Polza тихо игнорировал). Для не-whisper моделей идём через
        // json и нормализатор оценивает segment timestamps по тексту.
        bool supportsVerboseJson = request.Model.Contains("whisper", StringComparison.OrdinalIgnoreCase);
        if (supportsVerboseJson)
        {
            content.Add(new StringContent("verbose_json"), "response_format");
            // timestamp_granularities ДОЛЖЕН быть array — Polza 400'нёт single-value.
            // В multipart array выражается через `key[]`.
            content.Add(new StringContent("segment"), "timestamp_granularities[]");
        }
        else
        {
            content.Add(new StringContent("json"), "response_format");
        }

        if (!string.IsNullOrWhiteSpace(request.LanguageHint))
            content.Add(new StringContent(request.LanguageHint), "language");

        if (!string.IsNullOrWhiteSpace(request.Prompt))
            content.Add(new StringContent(request.Prompt), "prompt");

        if (_options.SendProviderRoutingHints)
        {
            string providerJson = JsonSerializer.Serialize(
                new { allow_fallbacks = true, sort = "throughput" },
                _jsonOptions);
            content.Add(new StringContent(providerJson, Encoding.UTF8, "application/json"), "provider");
        }

        // RFC 7578 multipart/form-data: имя параметра Content-Disposition можно писать
        // как quoted-string OR token. .NET по умолчанию шлёт unquoted (`name=file`),
        // но AITunnel parser требует quoted (`name="file"`) и режектит unquoted с
        // «Content-Disposition header in FormData part is missing a name». Также .NET
        // добавляет filename*=utf-8''... (RFC 5987) — AITunnel это поле игнорирует
        // и оно вызывает confusion в их парсере. Принудительно дописываем кавычки
        // и убираем filename*.
        ForceQuotedContentDispositionNames(content);

        // Per-call timeout через linked CTS, а не HttpClient.Timeout — иначе Polly
        // retry × 5 attempts × HttpClient.Timeout даёт worst-case timeoutSeconds × 5
        // visible hang. С linked CTS общий бюджет жёстко ограничен timeoutSeconds.
        using CancellationTokenSource timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        HttpClient httpClient = _httpClientFactory.CreateClient(OpenAiCompatibleHttpClients.TRANSCRIPTION);

        // Authorization кладём в HttpRequestMessage, а не на DefaultRequestHeaders —
        // фабрика возвращает per-call HttpClient (handler pooled), но мутировать
        // shared-стейт client'а небезопасно если регистрация когда-то станет singleton.
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content,
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(requestMessage, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                _logger.LogWarning(
                    "Transcription API returned {StatusCode} for model {Model}: {Body}",
                    response.StatusCode,
                    request.Model,
                    body.Length > 500 ? body[..500] : body);

                return Error.Failure(
                    "ai.transcription.failed",
                    $"Сервис транскрипции вернул ошибку: {(int)response.StatusCode}");
            }

            string responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            VerboseJsonResponse? parsed = JsonSerializer.Deserialize<VerboseJsonResponse>(responseBody, _jsonOptions);

            if (parsed is null)
                return Error.Failure("ai.transcription.invalid", "Не удалось распарсить ответ STT");

            AiTranscriptionSegment[] providerSegments = (parsed.Segments ?? [])
                .Select(s => new AiTranscriptionSegment(s.Start, s.End, s.Text?.Trim() ?? string.Empty))
                .ToArray();

            Result<double, Error> duration = ResolveDurationSeconds(parsed.Duration, providerSegments);
            if (duration.IsFailure)
                return duration.Error;

            AiTranscriptionSegment[] segments = providerSegments
                .Where(s => !string.IsNullOrEmpty(s.Text))
                .ToArray();

            return new AiTranscriptionResult(
                PROVIDER_NAME,
                request.Model,
                string.IsNullOrWhiteSpace(parsed.Language) ? "ru" : parsed.Language,
                ResolveFullText(parsed.Text, segments),
                segments,
                duration.Value);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Transcription request timed out after {Seconds}s for model {Model}",
                timeoutSeconds, request.Model);
            return Error.Failure("ai.transcription.timeout", "Сервис транскрипции не ответил вовремя");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Transcription request failed for model {Model}", request.Model);
            return Error.Failure("ai.transcription.network", "Сетевая ошибка при запросе транскрипции");
        }
    }

    private static Uri NormalizeBaseUrl(string baseUrl) =>
        new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    /// <summary>
    ///     .NET <see cref="MultipartFormDataContent.Add(HttpContent, string, string)"/>
    ///     сериализует Content-Disposition без кавычек вокруг <c>name=</c> и <c>filename=</c>
    ///     если значения — token chars (RFC 5234). Это RFC 7578-valid форма, но AITunnel
    ///     парсер строже и принимает только quoted-string. Также .NET добавляет
    ///     RFC 5987 <c>filename*=utf-8''...</c> — AITunnel это поле игнорирует, но
    ///     иногда парсер на нём спотыкается. Принудительно нормализуем оба поля.
    ///     <para>Idempotent — safe to call повторно (e.g. на Polly retry): wrap+trim
    ///     даёт тот же result на уже-quoted значении.</para>
    ///     <para>Embedded quotes внутри value: <see cref="QuoteEscape"/> escape'ит их.
    ///     В нашем случае name'ы захардкожены ASCII токенами, а filename — либо
    ///     <see cref="AiTranscriptionRequest.FileName"/> с предсказуемым source-path
    ///     basename, либо fallback <c>audio.mp3</c> — embedded quote'ов не будет,
    ///     но escape — defence-in-depth.</para>
    /// </summary>
    private static void ForceQuotedContentDispositionNames(MultipartFormDataContent content)
    {
        foreach (HttpContent part in content)
        {
            System.Net.Http.Headers.ContentDispositionHeaderValue? cd = part.Headers.ContentDisposition;
            if (cd is null)
                continue;

            if (cd.Name is { } name)
                cd.Name = QuoteEscape(name);

            if (cd.FileName is { } fileName)
                cd.FileName = QuoteEscape(fileName);

            // RFC 5987 extended filename — drop, AITunnel парсеру он мешает.
            cd.FileNameStar = null;
        }
    }

    private static string QuoteEscape(string value)
    {
        // Trim outer quotes (idempotent), escape any embedded backslash/quote per RFC 7230 quoted-string.
        string unwrapped = value.Trim('"');
        string escaped = unwrapped
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    /// <summary>
    ///     OpenAI standard — <c>audio/mpeg</c>; Polza исторически предпочитала
    ///     <c>audio/mp3</c>. На multipart обе формы Polza принимает (verified),
    ///     но MIME должен быть валидный — нормализуем пустой/whitespace в mp3.
    /// </summary>
    private static string NormalizeContentType(string contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? "audio/mpeg" : contentType;

    /// <summary>
    ///     Полный текст транскрипта — из top-level <c>"text"</c>. Некоторые провайдеры (наблюдалось
    ///     у AITunnel <c>whisper-1</c>) возвращают сегменты, но ПУСТОЙ/отсутствующий aggregate
    ///     <c>"text"</c> — тогда склеиваем его из сегментов через пробел. Без этого транскрипт
    ///     терялся и открытый ответ записывался пустым («Твой ответ —»), хотя речь распозналась (#568).
    /// </summary>
    public static string ResolveFullText(string? aggregateText, IReadOnlyList<AiTranscriptionSegment> segments)
    {
        string fullText = aggregateText?.Trim() ?? string.Empty;
        if (fullText.Length == 0 && segments.Count > 0)
            fullText = string.Join(" ", segments.Select(s => s.Text));
        return fullText;
    }

    public static Result<double, Error> ResolveDurationSeconds(
        double? reportedDuration,
        IReadOnlyList<AiTranscriptionSegment> segments)
    {
        if (reportedDuration is { } reported && (!double.IsFinite(reported) || reported < 0))
            return Error.Failure("ai.transcription.invalid", "STT вернул некорректную длительность аудио");

        double segmentDuration = 0;
        foreach (AiTranscriptionSegment segment in segments)
        {
            if (!double.IsFinite(segment.StartSeconds)
                || !double.IsFinite(segment.EndSeconds)
                || segment.StartSeconds < 0
                || segment.EndSeconds < segment.StartSeconds)
            {
                return Error.Failure("ai.transcription.invalid", "STT вернул некорректные таймкоды");
            }

            segmentDuration = Math.Max(segmentDuration, segment.EndSeconds);
        }

        // Text-only JSON responses omit duration and segments. Zero means unavailable.
        if (reportedDuration is null && segments.Count == 0)
            return 0;

        double duration = Math.Max(reportedDuration ?? 0, segmentDuration);
        return duration > 0
            ? duration
            : Error.Failure("ai.transcription.invalid", "STT не вернул длительность аудио");
    }

    private sealed record VerboseJsonResponse(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("duration")] double? Duration,
        [property: JsonPropertyName("segments")] VerboseJsonSegment[]? Segments);

    private sealed record VerboseJsonSegment(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("start")] double Start,
        [property: JsonPropertyName("end")] double End,
        [property: JsonPropertyName("text")] string? Text);
}
