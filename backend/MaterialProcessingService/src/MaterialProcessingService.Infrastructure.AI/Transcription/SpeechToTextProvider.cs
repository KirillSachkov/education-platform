using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared.AI;
using SharedKernel;
using MaterialProcessingService.Core.AiSettings;
using MaterialProcessingService.Core.Media;
using MaterialProcessingService.Core.Transcripts;

namespace MaterialProcessingService.Infrastructure.AI.Transcription;

/// <summary>
///     STT через dedicated <c>/v1/audio/transcriptions</c> endpoint провайдера
///     (OpenAI-compatible: AITunnel/Polza/OpenAI direct/ProxyAPI). RouterAI
///     отсутствует — у них нет dedicated transcription endpoint. Текущая модель —
///     настраиваема через <c>VideoProcessingAI:SpeechToText:Model</c>, провайдер
///     — через <c>VideoProcessingAI:SpeechToText:Provider</c>; рекомендуемые
///     значения моделей:
///     <list type="bullet">
///         <item><c>gpt-4o-mini-transcribe</c> — best price/quality для русского</item>
///         <item><c>gpt-4o-transcribe</c> — точнее, дороже</item>
///         <item><c>whisper-1</c> — стабильный whisper-классик, нативные segment timestamps</item>
///     </list>
///     В отличие от прежней реализации через <c>gpt-4o-audio-preview</c> (chat-API
///     с audio input + JSON Schema):
///     <list type="bullet">
///         <item>Цена в минутах аудио, не audio-tokens — предсказуемый биллинг.</item>
///         <item>Нативные timestamps на уровне сегментов через <c>verbose_json</c>.</item>
///         <item>Нет hallucination JSON Schema — ответ имеет фиксированный shape.</item>
///         <item>Языковой hint (<c>language=ru</c>) снижает галлюцинации
///             английских технических терминов в кириллице.</item>
///     </list>
/// </summary>
internal sealed class SpeechToTextProvider : ISpeechToTextProvider
{
    private readonly IAiTranscriptionClientFactory _transcriptionClientFactory;
    private readonly IAiModelSettingsResolver _settingsResolver;
    private readonly ILogger<SpeechToTextProvider> _logger;

    public SpeechToTextProvider(
        IAiTranscriptionClientFactory transcriptionClientFactory,
        IAiModelSettingsResolver settingsResolver,
        ILogger<SpeechToTextProvider> logger)
    {
        _transcriptionClientFactory = transcriptionClientFactory;
        _settingsResolver = settingsResolver;
        _logger = logger;
    }

    public async Task<Result<SpeechToTextResult, Error>> TranscribeAsync(
        AudioChunk chunk,
        string? modelOverride,
        CancellationToken cancellationToken)
    {
        byte[] audioBytes = await File.ReadAllBytesAsync(chunk.Path, cancellationToken);

        EffectiveAiModelSettings settings = await _settingsResolver.GetAsync(cancellationToken);
        EffectiveAiModelSlot sttSlot = settings.SpeechToText;
        // STT API (Whisper-style /v1/audio/transcriptions) принимает только model и timeout.
        // Temperature и MaxOutputTokens из slot'а игнорируются — они актуальны только для chat-LLM.
        string model = string.IsNullOrWhiteSpace(modelOverride) ? sttSlot.Model : modelOverride.Trim();

        // Provider resolved через factory — может бросить InvalidOperationException
        // если провайдер не зарегистрирован или не поддерживает STT (RouterAI).
        // Лучше упасть рано с понятным сообщением, чем дать клиенту обработать null.
        IAiTranscriptionClient transcriptionClient;
        try
        {
            transcriptionClient = _transcriptionClientFactory.Get(sttSlot.Provider);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(
                ex,
                "STT provider '{Provider}' is not available for transcription",
                sttSlot.Provider);
            return Error.Failure(
                "timecodes.transcription.provider_unavailable",
                $"AI-провайдер '{sttSlot.Provider}' недоступен для транскрипции. Проверьте AI:Providers в config.");
        }

        Result<AiTranscriptionResult, Error> transcriptionResult =
            await transcriptionClient.TranscribeAsync(
                new AiTranscriptionRequest(
                    Model: model,
                    FileName: Path.GetFileName(chunk.Path),
                    ContentType: "audio/mpeg",
                    AudioBytes: audioBytes,
                    LanguageHint: "ru",
                    // STT prompt — anchor для распознавания технических терминов.
                    // Whisper-style модели (gpt-4o-(mini-)transcribe) реагируют на
                    // буквальные вхождения слов в prompt'е, поэтому перечисляем
                    // конкретные имена сервисов/технологий.
                    //
                    // ОГРАНИЧЕНИЕ: Whisper `initial_prompt` лимит — 224 токена в
                    // gpt2 tokenizer (cyrillic дорогой: ~1.5-2 token/char). Текущий
                    // prompt — 163 токена (мерил `tiktoken get_encoding('gpt2')`).
                    // На overflow модель silently truncate'ит — glossary молча
                    // ломается. При расширении списка — обязательно мерять.
                    Prompt:
                        "Учебная лекция по программированию. Сохраняй английские термины как есть: " +
                        "Directory Service, File Service, Inventory Service, Auth Service, Order Service, " +
                        ".NET, ASP.NET Core, EF Core, Wolverine, gRPC, REST, JSON, " +
                        "PostgreSQL, Redis, RabbitMQ, Docker, Kubernetes, Nginx, " +
                        "TypeScript, React, Next.js, GitHub, GitLab, Pull Request, Pet Family, microservice, CamelCase.",
                    TimeoutSeconds: sttSlot.TimeoutSeconds),
                cancellationToken);

        if (transcriptionResult.IsFailure)
        {
            _logger.LogWarning(
                "Speech-to-text failed: {Error}",
                transcriptionResult.Error.GetMessage());

            return Error.Failure("timecodes.transcription.failed", "Не удалось распознать аудиодорожку видео");
        }

        AiTranscriptionResult payload = transcriptionResult.Value;

        // Адаптируем формат провайдера к локальному SpeechToTextSegmentResponse,
        // чтобы переиспользовать существующий нормализатор (clamp, ordering, fallback).
        SpeechToTextSegmentResponse[] adapterSegments = payload.Segments
            .Select(s => new SpeechToTextSegmentResponse(s.StartSeconds, s.EndSeconds, s.Text))
            .ToArray();

        // STT видит physical (atempo'нутый) аудиофайл — все timestamps идут в
        // trimmed-time. Нормализатору отдаём physical duration, чтобы fallback
        // EstimateFromText распределил предложения по реальной длине файла.
        // Маппинг trimmed → original делает TranscriptPreparationService через
        // chunk.Speedup (домножает start/end перед добавлением chunk.Offset).
        TimeSpan physicalDuration = TimeSpan.FromSeconds(chunk.Duration.TotalSeconds / chunk.Speedup);

        // gpt-4o-transcribe / gpt-4o-mini-transcribe не поддерживают
        // `verbose_json`-segments — игнорируют `timestamp_granularities[]` и возвращают
        // только `text`. В этом случае дробим full text по предложениям и
        // распределяем по времени пропорционально длине (TimestampSource=Estimated).
        SpeechTranscriptNormalizationResult normalizationResult =
            adapterSegments.Length == 0 && !string.IsNullOrWhiteSpace(payload.FullText)
                ? SpeechTranscriptNormalizer.EstimateFromText(payload.FullText, physicalDuration)
                : SpeechTranscriptNormalizer.NormalizeOrEstimate(adapterSegments, physicalDuration);
        TranscriptSegment[] segments = normalizationResult.Segments;

        if (segments.Length == 0)
            return Error.Failure("timecodes.transcription.empty", "Не удалось получить текст расшифровки");

        return new SpeechToTextResult(
            string.IsNullOrWhiteSpace(payload.Language) ? "ru" : payload.Language,
            segments,
            normalizationResult.TimestampSource);
    }
}
