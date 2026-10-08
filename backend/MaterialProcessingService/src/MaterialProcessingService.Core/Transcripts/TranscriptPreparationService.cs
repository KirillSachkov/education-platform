using Core.Database;
using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.AI;
using SharedKernel;
using MaterialProcessingService.Core.Media;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Transcripts;
using MaterialProcessingService.Domain.Transcripts.ValueObjects;

namespace MaterialProcessingService.Core.Transcripts;

public sealed class TranscriptPreparationService
{
    /// <summary>
    ///     Если STT-сегменты заканчиваются раньше чем (THRESHOLD × chunk.Duration), считаем
    ///     что произошла truncation (упёрлись в MaxOutputTokens или внутренний лимит провайдера).
    ///     В этом случае делим chunk пополам и пере-транскрибируем рекурсивно. Issue #110.
    /// </summary>
    private const double TRUNCATION_THRESHOLD = 0.7;

    /// <summary>
    ///     Глубина recursion'а сплита: 15 мин → 7.5 → 3.75. На 3.75 ещё truncate'ed → fail.
    ///     Дальше резать смысла нет — речь действительно слишком плотная для модели.
    /// </summary>
    private const int MAX_SPLIT_DEPTH = 2;

    private readonly IVideoTranscriptRepository _transcriptRepository;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IMediaProbe _mediaProbe;
    private readonly IAudioExtractor _audioExtractor;
    private readonly ISpeechToTextProvider _speechToTextProvider;
    private readonly ITransactionManager _transactionManager;
    private readonly VideoProcessingOptions _options;
    private readonly AiPipelineMetrics _metrics;
    private readonly ILogger<TranscriptPreparationService> _logger;

    public TranscriptPreparationService(
        IVideoTranscriptRepository transcriptRepository,
        IFileServiceClient fileServiceClient,
        IMediaProbe mediaProbe,
        IAudioExtractor audioExtractor,
        ISpeechToTextProvider speechToTextProvider,
        ITransactionManager transactionManager,
        IOptions<VideoProcessingOptions> options,
        AiPipelineMetrics metrics,
        ILogger<TranscriptPreparationService> logger)
    {
        _transcriptRepository = transcriptRepository;
        _fileServiceClient = fileServiceClient;
        _mediaProbe = mediaProbe;
        _audioExtractor = audioExtractor;
        _speechToTextProvider = speechToTextProvider;
        _transactionManager = transactionManager;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<Result<TranscriptPreparationResult, Error>> PrepareAsync(
        TranscriptPreparationRequest request,
        Func<TranscriptPreparationStage, int, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        VideoTranscript? cachedTranscript = await _transcriptRepository.GetByVideoAssetVersionAsync(
            request.VideoAssetId,
            request.AssetVersion,
            asNoTracking: true,
            cancellationToken: cancellationToken);

        if (cachedTranscript is not null)
        {
            _logger.LogInformation(
                "Using cached transcript for video {VideoId} asset version {AssetVersion}",
                request.VideoAssetId,
                request.AssetVersion);

            return new TranscriptPreparationResult(
                MapTranscript(cachedTranscript),
                TimeSpan.FromSeconds(cachedTranscript.Duration.Seconds));
        }

        Result<GetVideoProcessingSourceResponse?, Error> sourceResult =
            await _fileServiceClient.GetVideoProcessingSourceAsync(request.VideoAssetId, cancellationToken);

        if (sourceResult.IsFailure)
            return sourceResult.Error;

        if (sourceResult.Value is null)
            return GeneralErrors.NotFound(request.VideoAssetId);

        VideoProcessingSource source = new(
            sourceResult.Value.VideoId,
            sourceResult.Value.AssetVersion,
            sourceResult.Value.DurationSeconds,
            sourceResult.Value.SourceType,
            sourceResult.Value.Url,
            sourceResult.Value.ExpiresAt);

        await reportProgressAsync(TranscriptPreparationStage.Probe, 10, cancellationToken);

        Result<MediaProbeResult, Error> probeResult = await _mediaProbe.ProbeAsync(source, cancellationToken);
        if (probeResult.IsFailure)
            return probeResult.Error;

        if (!probeResult.Value.HasAudioStream)
        {
            return Error.Validation("video.audio_stream_not_found", "В видео не найдена аудиодорожка");
        }

        if (probeResult.Value.Duration > TimeSpan.FromMinutes(_options.MaxVideoDurationMinutes))
        {
            return Error.Validation("video.duration.too_large", "Видео превышает максимально допустимую длительность");
        }

        await reportProgressAsync(TranscriptPreparationStage.AudioExtract, 25, cancellationToken);

        Result<IReadOnlyList<AudioChunk>, Error> chunksResult =
            await _audioExtractor.ExtractChunksAsync(source, request.ProcessingId, cancellationToken);

        if (chunksResult.IsFailure)
            return chunksResult.Error;

        Result<Transcript, Error> transcriptResult = await BuildTranscriptAsync(
            chunksResult.Value,
            request.ProcessingId,
            request.EmptyTranscriptErrorCode,
            request.SttModelOverride,
            reportProgressAsync,
            cancellationToken);

        if (transcriptResult.IsFailure)
            return transcriptResult.Error;

        Transcript transcript = transcriptResult.Value;
        TimeSpan videoDuration = probeResult.Value.Duration;

        Result<VideoTranscript, Error> persistedTranscriptResult = CreateVideoTranscript(
            request.VideoAssetId,
            request.AssetVersion,
            videoDuration,
            transcript);

        if (persistedTranscriptResult.IsFailure)
            return persistedTranscriptResult.Error;

        await _transcriptRepository.AddAsync(persistedTranscriptResult.Value, cancellationToken);

        UnitResult<Error> saveTranscriptResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveTranscriptResult.IsFailure)
        {
            VideoTranscript? persistedTranscript = await _transcriptRepository.GetByVideoAssetVersionAsync(
                request.VideoAssetId,
                request.AssetVersion,
                asNoTracking: true,
                cancellationToken: cancellationToken);

            if (persistedTranscript is not null)
            {
                _logger.LogInformation(
                    "Reusing transcript persisted concurrently for video {VideoId} asset version {AssetVersion}",
                    request.VideoAssetId,
                    request.AssetVersion);

                return new TranscriptPreparationResult(
                    MapTranscript(persistedTranscript),
                    TimeSpan.FromSeconds(persistedTranscript.Duration.Seconds));
            }

            return saveTranscriptResult.Error;
        }

        return new TranscriptPreparationResult(transcript, videoDuration);
    }

    private async Task<Result<Transcript, Error>> BuildTranscriptAsync(
        IReadOnlyList<AudioChunk> chunks,
        Guid jobId,
        string emptyTranscriptErrorCode,
        string? sttModelOverride,
        Func<TranscriptPreparationStage, int, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        List<TranscriptSegment> transcriptSegments = [];
        string transcriptLanguage = _options.DefaultLanguage;

        for (int i = 0; i < chunks.Count; i++)
        {
            AudioChunk chunk = chunks[i];

            int progress = 25 + (int)Math.Round(((i + 1d) / Math.Max(chunks.Count, 1)) * 50d);
            await reportProgressAsync(TranscriptPreparationStage.Transcribe, progress, cancellationToken);

            Result<TranscribedChunk, Error> chunkResult = await TranscribeChunkWithDefenseAsync(
                chunk,
                jobId,
                sttModelOverride,
                level: 0,
                cancellationToken);

            if (chunkResult.IsFailure)
                return chunkResult.Error;

            if (!string.IsNullOrWhiteSpace(chunkResult.Value.Language))
                transcriptLanguage = chunkResult.Value.Language;

            transcriptSegments.AddRange(chunkResult.Value.Segments);
        }

        if (transcriptSegments.Count == 0)
        {
            return Error.Validation(emptyTranscriptErrorCode, "Не удалось получить расшифровку видео");
        }

        Transcript transcript = new(
            transcriptLanguage,
            transcriptSegments.OrderBy(x => x.Start).ToArray());

        return transcript;
    }

    /// <summary>
    ///     Транскрибирует один аудио-чанк со встроенной защитой от truncation
    ///     (issue #110). Если последний segment.End оказался сильно раньше конца чанка —
    ///     это сигнал что STT упёрся в лимит и обрезал хвост; в этом случае делим
    ///     чанк пополам через <see cref="IAudioExtractor.SplitChunkAsync"/> и
    ///     рекурсивно транскрибируем каждую половину.
    /// </summary>
    /// <param name="chunk">Текущий чанк (offset уже в координатах исходного видео).</param>
    /// <param name="jobId">ID job'а — нужен для namespace'а split-файлов в FFmpeg temp-каталоге.</param>
    /// <param name="sttModelOverride">Admin per-job override модели (если задан).</param>
    /// <param name="level">Глубина recursion'а: 0 = первичный вызов; max <see cref="MAX_SPLIT_DEPTH"/>.</param>
    private async Task<Result<TranscribedChunk, Error>> TranscribeChunkWithDefenseAsync(
        AudioChunk chunk,
        Guid jobId,
        string? sttModelOverride,
        int level,
        CancellationToken cancellationToken)
    {
        Result<SpeechToTextResult, Error> transcribeResult =
            await _speechToTextProvider.TranscribeAsync(chunk, sttModelOverride, cancellationToken);

        if (transcribeResult.IsFailure)
            return transcribeResult.Error;

        SpeechToTextResult payload = transcribeResult.Value;
        // STT отдаёт сегменты в координатах PHYSICAL chunk-файла (после atempo
        // ускорения), а chunk.Offset — в координатах ОРИГИНАЛЬНОГО видео.
        // Чтобы получить global original time: сначала растягиваем chunk-local
        // время на speedup (trimmed → original chunk-local), затем добавляем offset.
        // При chunk.Speedup=1.0 (без ускорения) формула вырождается в +chunk.Offset.
        TranscriptSegment[] absoluteSegments = payload.Segments
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .Select(s => new TranscriptSegment(
                TimeSpan.FromSeconds(s.Start.TotalSeconds * chunk.Speedup) + chunk.Offset,
                TimeSpan.FromSeconds(s.End.TotalSeconds * chunk.Speedup) + chunk.Offset,
                s.Text.Trim()))
            .ToArray();

        bool truncationSuspected = SuspectsTruncation(absoluteSegments, chunk);

        if (!truncationSuspected)
            return new TranscribedChunk(payload.Language ?? string.Empty, absoluteSegments);

        _metrics.RecordTruncation(job: "STT", level: level);

        if (level >= MAX_SPLIT_DEPTH)
        {
            double lastEndRelative = absoluteSegments.Length > 0
                ? absoluteSegments[^1].End.TotalSeconds - chunk.Offset.TotalSeconds
                : 0;

            _logger.LogWarning(
                "STT truncation persists at max recursion level {Level} for chunk offset={Offset}s duration={Duration}s. " +
                "Last segment end relative to chunk: {LastEnd}s of {Limit}s.",
                level,
                chunk.Offset.TotalSeconds,
                chunk.Duration.TotalSeconds,
                lastEndRelative,
                chunk.Duration.TotalSeconds);

            return Error.Failure(
                "stt.audio_too_dense",
                "Аудио слишком плотное для STT-модели. Попробуйте укоротить видео или сменить модель.");
        }

        _logger.LogInformation(
            "STT truncation detected for chunk offset={Offset}s duration={Duration}s (level {Level}). Splitting in half and re-transcribing.",
            chunk.Offset.TotalSeconds,
            chunk.Duration.TotalSeconds,
            level);

        Result<IReadOnlyList<AudioChunk>, Error> splitResult =
            await _audioExtractor.SplitChunkAsync(chunk, parts: 2, jobId, cancellationToken);

        if (splitResult.IsFailure)
            return splitResult.Error;

        _metrics.RecordChunkSplit();

        List<TranscriptSegment> mergedSegments = [];
        string lang = payload.Language ?? string.Empty;

        foreach (AudioChunk part in splitResult.Value)
        {
            Result<TranscribedChunk, Error> partResult = await TranscribeChunkWithDefenseAsync(
                part,
                jobId,
                sttModelOverride,
                level + 1,
                cancellationToken);

            if (partResult.IsFailure)
                return partResult.Error;

            if (!string.IsNullOrWhiteSpace(partResult.Value.Language))
                lang = partResult.Value.Language;

            mergedSegments.AddRange(partResult.Value.Segments);
        }

        return new TranscribedChunk(lang, mergedSegments);
    }

    private static bool SuspectsTruncation(
        IReadOnlyList<TranscriptSegment> absoluteSegments,
        AudioChunk chunk)
    {
        if (absoluteSegments.Count == 0)
            return false; // empty transcript — distinct condition, обрабатывается отдельным error code

        // Защита от мелких чанков (< 60 сек): split малоосмыслен, threshold ненадёжен,
        // плюс короткие тестовые mocks с 2-мин chunk и фразой в 30 секунд НЕ должны
        // считаться truncation — у них реально мало речи, не обрезка.
        if (chunk.Duration.TotalSeconds < 60)
            return false;

        // Дополнительный guard: считаем truncation только если в transcript
        // ВООБЩЕ что-то распознали (общая длина текста > 200 chars). Иначе скорее всего
        // тихое аудио / музыка / короткий фрагмент — split не нужен.
        int totalChars = 0;
        for (int i = 0; i < absoluteSegments.Count; i++)
            totalChars += absoluteSegments[i].Text.Length;

        if (totalChars < 200)
            return false;

        TimeSpan threshold = chunk.Offset + TimeSpan.FromSeconds(chunk.Duration.TotalSeconds * TRUNCATION_THRESHOLD);
        TimeSpan lastSegmentEnd = absoluteSegments[^1].End;
        return lastSegmentEnd < threshold;
    }

    private readonly record struct TranscribedChunk(
        string Language,
        IReadOnlyList<TranscriptSegment> Segments);

    private static Transcript MapTranscript(VideoTranscript transcript)
    {
        TranscriptSegment[] segments = transcript.Segments.Items
            .Where(segment => !string.IsNullOrWhiteSpace(segment.Text.Value))
            .OrderBy(segment => segment.Range.StartSeconds)
            .Select(segment => new TranscriptSegment(
                TimeSpan.FromSeconds(segment.Range.StartSeconds),
                TimeSpan.FromSeconds(segment.Range.EndSeconds),
                segment.Text.Value))
            .ToArray();

        return new Transcript(transcript.Language.Value, segments);
    }

    private static Result<VideoTranscript, Error> CreateVideoTranscript(
        Guid videoAssetId,
        Guid assetVersion,
        TimeSpan duration,
        Transcript transcript)
    {
        Result<TranscriptDuration, Error> durationResult = TranscriptDuration.Create(duration);
        if (durationResult.IsFailure)
            return durationResult.Error;

        Result<Language, Error> languageResult = Language.Create(transcript.Language);
        if (languageResult.IsFailure)
            return languageResult.Error;

        List<VideoTranscriptSegment> segments = [];
        foreach (TranscriptSegment segment in transcript.Segments)
        {
            // Defensive: skip degenerate segments (end <= start). Особенно актуально
            // когда EstimateFromText на последнем предложении упирается в границу
            // chunk'а — `consumedSeconds == totalSeconds` даёт zero-duration range,
            // которое не проходит TranscriptSegmentRange.Create.
            if (segment.End <= segment.Start)
                continue;

            Result<TranscriptSegmentRange, Error> rangeResult = TranscriptSegmentRange.Create(
                segment.Start.TotalSeconds,
                segment.End.TotalSeconds);
            if (rangeResult.IsFailure)
                continue;

            Result<TranscriptSegmentText, Error> textResult = TranscriptSegmentText.Create(segment.Text);
            if (textResult.IsFailure)
                continue;

            Result<VideoTranscriptSegment, Error> segmentResult = VideoTranscriptSegment.Create(
                rangeResult.Value,
                textResult.Value);
            if (segmentResult.IsFailure)
                continue;

            segments.Add(segmentResult.Value);
        }

        Result<TranscriptSegments, Error> segmentsResult = TranscriptSegments.Create(segments);
        if (segmentsResult.IsFailure)
            return segmentsResult.Error;

        return VideoTranscript.Create(
            videoAssetId,
            assetVersion,
            durationResult.Value,
            languageResult.Value,
            segmentsResult.Value);
    }
}
