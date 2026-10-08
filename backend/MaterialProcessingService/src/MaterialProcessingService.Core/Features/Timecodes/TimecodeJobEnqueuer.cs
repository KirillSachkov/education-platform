using Core.Database;
using CSharpFunctionalExtensions;
using SharedKernel;
using MaterialProcessingService.Core.Database;
using MaterialProcessingService.Core.Features.Timecodes.Processing;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Timecodes;

namespace MaterialProcessingService.Core.Features.Timecodes;

public enum TimecodeEnqueueOutcome
{
    /// <summary>Создан новый QUEUED-job, опубликовано сообщение в pipeline.</summary>
    Created,

    /// <summary>Для видео уже есть активный (QUEUED/PROCESSING) job — вернули его.</summary>
    AlreadyActive,

    /// <summary>
    ///     <c>dedupByVersion=true</c> и для <c>(videoAssetId, assetVersion)</c> уже
    ///     существует job (любого статуса) — пропущено. Только авто-путь.
    /// </summary>
    SkippedAlreadyProcessed,
}

/// <summary>
///     Результат постановки. <b>Сначала проверяй <see cref="Outcome"/></b>: при
///     <see cref="TimecodeEnqueueOutcome.SkippedAlreadyProcessed"/> поля <see cref="JobId"/> =
///     <see cref="Guid.Empty"/> и <see cref="Status"/> = заглушка (job не трогали). Для
///     <c>Created</c>/<c>AlreadyActive</c> оба поля валидны и указывают на реальный job.
/// </summary>
public sealed record TimecodeEnqueueResult(
    TimecodeEnqueueOutcome Outcome,
    Guid JobId,
    TimecodeGenerationStatus Status);

/// <summary>
///     Общее ядро постановки timecode-job'а, переиспользуемое ручным эндпоинтом
///     (<c>POST .../timecode-generations/</c>) и авто-консьюмером готовности видео
///     (<c>VideoReadyForProcessingHandler</c>). Владеет: active-dedup, опциональным
///     version-dedup (для авто-идемпотентности), созданием job'а, outbox-publish и
///     recovery при гонке на partial unique index. Авторизация/ownership/rate-limit
///     и резолв source — на стороне caller'а. Issue #648.
/// </summary>
public sealed class TimecodeJobEnqueuer
{
    private readonly ITimecodeGenerationJobRepository _jobRepository;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;

    public TimecodeJobEnqueuer(
        ITimecodeGenerationJobRepository jobRepository,
        IOutboxService outboxService,
        ITransactionManager transactionManager)
    {
        _jobRepository = jobRepository;
        _outboxService = outboxService;
        _transactionManager = transactionManager;
    }

    public async Task<Result<TimecodeEnqueueResult, Error>> EnqueueAsync(
        Guid videoAssetId,
        Guid assetVersion,
        ProcessingSourceType sourceType,
        Guid requestedByUserId,
        string? modelOverride,
        TimecodeTriggerSource triggerSource,
        Guid? materialId,
        bool dedupByVersion,
        CancellationToken cancellationToken = default)
    {
        TimecodeGenerationJob? activeJob =
            await _jobRepository.GetActiveByVideoAsync(videoAssetId, cancellationToken);
        if (activeJob is not null)
            return new TimecodeEnqueueResult(TimecodeEnqueueOutcome.AlreadyActive, activeJob.Id, activeJob.Status);

        // Авто-идемпотентность: повторное VideoReadyForProcessing для той же версии
        // (re-delivery / republish) не должно плодить второй job. Ручной путь
        // (dedupByVersion=false) разрешает re-run для уже обработанной версии.
        if (dedupByVersion)
        {
            bool alreadyProcessed = await _jobRepository.ExistsAsync(
                x => x.VideoAssetId == videoAssetId && x.AssetVersion == assetVersion,
                cancellationToken);
            if (alreadyProcessed)
            {
                return new TimecodeEnqueueResult(
                    TimecodeEnqueueOutcome.SkippedAlreadyProcessed,
                    Guid.Empty,
                    TimecodeGenerationStatus.Completed);
            }
        }

        Result<TimecodeGenerationJob, Error> jobResult = TimecodeGenerationJob.Create(
            videoAssetId,
            assetVersion,
            requestedByUserId,
            sourceType,
            TimecodeGenerationJobMode.TIMECODES,
            modelOverride,
            triggerSource,
            materialId);
        if (jobResult.IsFailure)
            return jobResult.Error;

        TimecodeGenerationJob job = jobResult.Value;

        await _jobRepository.AddAsync(job, cancellationToken);
        await _outboxService.PublishAsync(new GenerateTimecodesJob(job.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            // Гонка на ux_timecode_jobs_video_active: параллельный enqueue успел первым.
            // Возвращаем его job вместо ошибки — caller'у важен факт, что обработка идёт.
            TimecodeGenerationJob? concurrentJob =
                await _jobRepository.GetActiveByVideoAsync(videoAssetId, cancellationToken);
            if (concurrentJob is not null)
                return new TimecodeEnqueueResult(TimecodeEnqueueOutcome.AlreadyActive, concurrentJob.Id, concurrentJob.Status);

            return saveResult.Error;
        }

        return new TimecodeEnqueueResult(TimecodeEnqueueOutcome.Created, job.Id, job.Status);
    }
}
