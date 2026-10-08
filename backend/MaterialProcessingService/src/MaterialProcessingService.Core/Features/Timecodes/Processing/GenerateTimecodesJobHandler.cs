using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Core.Database;
using SharedKernel;
using SharedKernel.Exceptions;
using Shared.Messaging.IntegrationEvents.MaterialProcessing.Events;
using MaterialProcessingService.Core.Database;
using MaterialProcessingService.Core.Media;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Core.Transcripts;
using MaterialProcessingService.Domain.Timecodes;

// Алиасы — конфликт типов с FileService.Contracts.Assets (одноимённые
// UpdateVideoChaptersRequest/VideoChapterDto). По правилу IDE0065 alias-using
// идёт последним в блоке.
using EcsChapterDto = EducationContentService.Contracts.Materials.VideoChapterDto;
using EcsUpdateVideoChaptersRequest = EducationContentService.Contracts.Materials.UpdateVideoChaptersRequest;

namespace MaterialProcessingService.Core.Features.Timecodes.Processing;

public sealed class GenerateTimecodesJobHandler
{
    private readonly ITimecodeGenerationJobRepository _jobRepository;
    private readonly IAudioExtractor _audioExtractor;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly TranscriptPreparationService _transcriptPreparationService;
    private readonly ITimecodeGenerator _timecodeGenerator;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outboxService;
    private readonly TimecodeGenerationOptions _options;
    private readonly ILogger<GenerateTimecodesJobHandler> _logger;

    public GenerateTimecodesJobHandler(
        ITimecodeGenerationJobRepository jobRepository,
        IAudioExtractor audioExtractor,
        IFileServiceClient fileServiceClient,
        IEducationContentServiceClient educationContentServiceClient,
        TranscriptPreparationService transcriptPreparationService,
        ITimecodeGenerator timecodeGenerator,
        ITransactionManager transactionManager,
        IOutboxService outboxService,
        IOptions<TimecodeGenerationOptions> options,
        ILogger<GenerateTimecodesJobHandler> logger)
    {
        _jobRepository = jobRepository;
        _audioExtractor = audioExtractor;
        _fileServiceClient = fileServiceClient;
        _educationContentServiceClient = educationContentServiceClient;
        _transcriptPreparationService = transcriptPreparationService;
        _timecodeGenerator = timecodeGenerator;
        _transactionManager = transactionManager;
        _outboxService = outboxService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Handle(GenerateTimecodesJob message, CancellationToken cancellationToken)
    {
        TimecodeGenerationJob? job = await _jobRepository.GetByAsync(
            x => x.Id == message.JobId,
            cancellationToken);

        if (job is null)
        {
            _logger.LogWarning("Timecode job {JobId} not found", message.JobId);
            return;
        }

        try
        {
            if (job.Status is TimecodeGenerationStatus.Completed or TimecodeGenerationStatus.Failed)
                return;

            UnitResult<Error> startResult = await StartJobAsync(job, cancellationToken);
            if (startResult.IsFailure)
                throw startResult.Error.ToException();

            UnitResult<Error> executionResult;
            try
            {
                executionResult = await ExecuteAsync(job, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TransientException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during timecode generation job {JobId}", job.Id);
                executionResult = UnitResult.Failure<Error>(
                    Error.Failure("timecodes.generation_failed", "Не удалось сгенерировать тайм-коды"));
            }

            if (executionResult.IsSuccess)
                return;

            if (executionResult.Error.Behavior == ErrorBehavior.Transient)
                throw executionResult.Error.ToException();

            UnitResult<Error> failResult = await FailJobAsync(
                job,
                executionResult.Error,
                CancellationToken.None);
            if (failResult.IsFailure)
                throw failResult.Error.ToException();
        }
        finally
        {
            await _audioExtractor.CleanupAsync(job.Id, CancellationToken.None);
        }
    }

    private async Task<UnitResult<Error>> ExecuteAsync(
        TimecodeGenerationJob job,
        CancellationToken cancellationToken)
    {
        // Для TRANSCRIPT_ONLY job ModelOverride интерпретируется как STT-модель,
        // для TIMECODES — как timecode-LLM-модель (STT использует default из конфига).
        // Это упрощает UI: один input, его смысл зависит от Mode.
        string? sttModelOverride = job.Mode == TimecodeGenerationJobMode.TRANSCRIPT_ONLY
            ? job.ModelOverride
            : null;
        string? timecodeModelOverride = job.Mode == TimecodeGenerationJobMode.TIMECODES
            ? job.ModelOverride
            : null;

        Result<TranscriptPreparationResult, Error> preparationResult =
            await _transcriptPreparationService.PrepareAsync(
                new TranscriptPreparationRequest(
                    job.VideoAssetId,
                    job.AssetVersion,
                    job.Id,
                    "timecodes.transcript.empty",
                    SttModelOverride: sttModelOverride),
                async (stage, progress, token) =>
                    // Сбой записи прогресс-стадии не роняет pipeline — discard осознанный.
                    _ = await UpdateStageAsync(
                        job,
                        MapTranscriptStage(stage),
                        progress,
                        token),
                cancellationToken);

        if (preparationResult.IsFailure)
            return preparationResult.Error;

        // TRANSCRIPT_ONLY mode: stop after transcript is prepared. Author may
        // separately request timecodes/content generation later — TranscriptPreparationService
        // returns the cached transcript by AssetVersion, so the heavy STT step is not repeated.
        if (job.Mode == TimecodeGenerationJobMode.TRANSCRIPT_ONLY)
            return await CompleteJobAsync(job, cancellationToken);

        UnitResult<Error> updateGenerateStageResult =
            await UpdateStageAsync(job, TimecodeGenerationStage.Generate, 85, cancellationToken);
        if (updateGenerateStageResult.IsFailure)
            return updateGenerateStageResult.Error;

        Result<GeneratedTimecodesResult, Error> generatedResult = await _timecodeGenerator.GenerateAsync(
            preparationResult.Value.Transcript,
            preparationResult.Value.VideoDuration,
            timecodeModelOverride,
            cancellationToken);
        if (generatedResult.IsFailure)
            return generatedResult.Error;

        Result<IReadOnlyList<GeneratedVideoTimecode>, Error> validationResult = ValidateGeneratedTimecodes(
            generatedResult.Value.Timecodes,
            preparationResult.Value.VideoDuration,
            _options.MinGapBetweenTimecodesSeconds);
        if (validationResult.IsFailure)
            return validationResult.Error;

        UnitResult<Error> updateSaveStageResult =
            await UpdateStageAsync(job, TimecodeGenerationStage.Save, 95, cancellationToken);
        if (updateSaveStageResult.IsFailure)
            return updateSaveStageResult.Error;

        UnitResult<Error> applyResult = await _fileServiceClient.UpdateChaptersAsync(
            job.VideoAssetId,
            new UpdateVideoChaptersRequest(
                job.Id,
                job.AssetVersion,
                validationResult.Value
                    .Select((timecode, index) => new UpdateVideoChapterItemDto(
                        timecode.Title,
                        timecode.StartSeconds,
                        index))
                    .ToArray()),
            cancellationToken);
        if (applyResult.IsFailure)
            return applyResult.Error;

        // Денормализуем главы видео (заголовок + offset) в ECS — материалы с этим videoId
        // получат chapter_titles + chapter_timestamps, SearchService переиндексирует
        // поле для полнотекстового поиска. Timestamp нужен фронту, чтобы deep-link на
        // нужную секунду в Kinescope-плеере.
        // Best-effort: ошибка ECS не валит job целиком (главы уже в Kinescope), просто warn.
        UnitResult<Error> denormalizeResult = await _educationContentServiceClient.UpdateVideoChaptersAsync(
            job.VideoAssetId,
            new EcsUpdateVideoChaptersRequest(
                job.AssetVersion,
                validationResult.Value.Select(t => new EcsChapterDto(t.Title, t.StartSeconds)).ToArray()),
            cancellationToken);
        if (denormalizeResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to denormalize chapters to ECS for video {VideoId}: {Error}. " +
                "Chapters are saved in Kinescope; search index will lag until next regeneration.",
                job.VideoAssetId,
                denormalizeResult.Error.GetMessage());
        }

        return await CompleteJobAsync(job, cancellationToken);
    }

    private async Task<UnitResult<Error>> StartJobAsync(
        TimecodeGenerationJob job,
        CancellationToken cancellationToken)
    {
        job.Start();
        return await SaveChangesAsync(cancellationToken);
    }

    private async Task<UnitResult<Error>> UpdateStageAsync(
        TimecodeGenerationJob job,
        TimecodeGenerationStage stage,
        int progressPercent,
        CancellationToken cancellationToken)
    {
        job.UpdateStage(stage, progressPercent);
        return await SaveChangesAsync(cancellationToken);
    }

    private async Task<UnitResult<Error>> CompleteJobAsync(
        TimecodeGenerationJob job,
        CancellationToken cancellationToken)
    {
        job.MarkCompleted();
        return await SaveChangesAsync(cancellationToken);
    }

    private async Task<UnitResult<Error>> FailJobAsync(
        TimecodeGenerationJob job,
        Error error,
        CancellationToken cancellationToken)
    {
        job.MarkFailed(error);

        // АВТО-запущенный job: автор кнопку не нажимал и не видит статус на странице —
        // публикуем событие, NotificationService шлёт ему in-app уведомление с дип-линком
        // на материал. Ручной job уведомление не шлёт (автор сам инициировал и видит ошибку).
        // Outbox flush'ится атомарно с MarkFailed в SaveChangesAsync ниже (issue #648).
        if (job.TriggerSource == TimecodeTriggerSource.AUTO && job.MaterialId is { } materialId)
        {
            await _outboxService.PublishAsync(new VideoAutoProcessingFailed(
                job.Id,
                materialId,
                job.VideoAssetId,
                job.RequestedByUserId,
                job.ErrorCode ?? "video.auto_processing.failed",
                job.ErrorMessage ?? "Не удалось обработать видео"));
        }

        CancellationToken persistCancellationToken = cancellationToken.IsCancellationRequested
            ? CancellationToken.None
            : cancellationToken;

        return await SaveChangesAsync(persistCancellationToken);
    }

    private async Task<UnitResult<Error>> SaveChangesAsync(CancellationToken cancellationToken)
    {
        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to persist timecode job state: {Error}",
                saveResult.Error.GetMessage());
        }

        return saveResult.IsFailure
            ? UnitResult.Failure<Error>(saveResult.Error.AsTransient())
            : saveResult;
    }

    private static TimecodeGenerationStage MapTranscriptStage(TranscriptPreparationStage stage) =>
        stage switch
        {
            TranscriptPreparationStage.Probe => TimecodeGenerationStage.Probe,
            TranscriptPreparationStage.AudioExtract => TimecodeGenerationStage.AudioExtract,
            TranscriptPreparationStage.Transcribe => TimecodeGenerationStage.Transcribe,
            _ => TimecodeGenerationStage.SourceFetch
        };

    private static Result<IReadOnlyList<GeneratedVideoTimecode>, Error> ValidateGeneratedTimecodes(
        IReadOnlyList<GeneratedVideoTimecode> generatedTimecodes,
        TimeSpan duration,
        int minGapBetweenTimecodesSeconds)
    {
        if (generatedTimecodes.Count == 0)
        {
            return Error.Validation("timecodes.empty", "Не удалось построить тайм-коды по расшифровке видео");
        }

        // Очищаем titles от U+FFFD (replacement char) и других битых символов,
        // которые могут прилететь от LLM если в transcript'е была кодировочная
        // грязь или модель сгенерировала недопустимые байты. Без sanitize
        // юзер видит «Реш��ние проблемы» в названии главы.
        List<GeneratedVideoTimecode> ordered = generatedTimecodes
            .Select(x => x with { Title = SanitizeTitle(x.Title) })
            .OrderBy(x => x.StartSeconds)
            .ToList();

        for (int index = 0; index < ordered.Count; index++)
        {
            GeneratedVideoTimecode current = ordered[index];

            if (current.StartSeconds < 0 || current.StartSeconds > duration.TotalSeconds)
            {
                return Error.Validation("timecodes.invalid_timestamp", "Тайм-код выходит за пределы длительности видео");
            }

            if (string.IsNullOrWhiteSpace(current.Title))
            {
                return Error.Validation("timecodes.title.required", "Заголовок тайм-кода обязателен");
            }

            if (index == 0)
                continue;

            GeneratedVideoTimecode previous = ordered[index - 1];
            if (current.StartSeconds - previous.StartSeconds < minGapBetweenTimecodesSeconds)
            {
                return Error.Validation("timecodes.too_dense", "Сгенерированные тайм-коды расположены слишком близко друг к другу");
            }
        }

        if (ordered[0].StartSeconds > 30)
        {
            ordered[0] = ordered[0] with { StartSeconds = 0 };
        }

        return ordered;
    }

    private static string SanitizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        // Strip U+FFFD (replacement character) и control-символы кроме whitespace.
        // Также collapse'им подряд идущие пробелы в один.
        var sb = new System.Text.StringBuilder(title.Length);
        bool lastWasSpace = false;
        foreach (char c in title)
        {
            if (c == '�')
                continue;
            if (char.IsControl(c) && c != '\t')
                continue;
            if (char.IsWhiteSpace(c))
            {
                if (lastWasSpace)
                    continue;
                sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }
}
