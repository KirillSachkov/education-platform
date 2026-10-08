using CSharpFunctionalExtensions;
using MaterialProcessingService.Domain.AiSettings;
using MaterialProcessingService.Domain.Common.ValueObjects;
using SharedKernel;

namespace MaterialProcessingService.Domain.Timecodes;

public sealed class TimecodeGenerationJob
{
    private TimecodeGenerationJob()
    {
    }

    private TimecodeGenerationJob(
        Guid id,
        Guid videoAssetId,
        Guid assetVersion,
        Guid requestedByUserId,
        ProcessingSourceType sourceType,
        TimecodeGenerationJobMode mode,
        string? modelOverride,
        TimecodeTriggerSource triggerSource,
        Guid? materialId)
    {
        Id = id;
        VideoAssetId = videoAssetId;
        AssetVersion = assetVersion;
        RequestedByUserId = requestedByUserId;
        SourceType = sourceType;
        Mode = mode;
        ModelOverride = modelOverride;
        TriggerSource = triggerSource;
        MaterialId = materialId;
        Status = TimecodeGenerationStatus.Queued;
        Stage = TimecodeGenerationStage.Queued;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public Guid VideoAssetId { get; private set; }

    public Guid AssetVersion { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public TimecodeGenerationStatus Status { get; private set; }

    public TimecodeGenerationStage Stage { get; private set; }

    public int ProgressPercent { get; private set; }

    public ProcessingSourceType SourceType { get; private set; } = null!;

    /// <summary>
    ///     Режим: полный pipeline (TIMECODES) или только подготовка транскрипта
    ///     (TRANSCRIPT_ONLY). Default — TIMECODES (legacy совместимость).
    /// </summary>
    public TimecodeGenerationJobMode Mode { get; private set; }

    /// <summary>
    ///     Optional admin-set model override (e.g. "openai/gpt-4o-mini-transcribe") для A/B
    ///     тестов на dev. Background handler читает значение → передаёт в AI client
    ///     вместо default из VideoProcessingAI:*:Model. NULL → используется конфиг.
    /// </summary>
    public string? ModelOverride { get; private set; }

    /// <summary>
    ///     Как был запущен job: вручную автором (<c>MANUAL</c>) или реактивно по
    ///     готовности видео (<c>AUTO</c>). Только AUTO-job при падении публикует
    ///     <c>VideoAutoProcessingFailed</c> (автор не нажимал кнопку — узнаёт об
    ///     ошибке из in-app уведомления). Issue #648.
    /// </summary>
    public TimecodeTriggerSource TriggerSource { get; private set; }

    /// <summary>
    ///     Материал, к которому привязано видео. Заполняется только для AUTO-job'ов
    ///     (из <c>VideoReadyForProcessing.TargetEntityId</c>) — нужен для дип-линка
    ///     в уведомлении о сбое. Для ручного запуска NULL. Issue #648.
    /// </summary>
    public Guid? MaterialId { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<TimecodeGenerationJob, Error> Create(
        Guid videoAssetId,
        Guid assetVersion,
        Guid requestedByUserId,
        ProcessingSourceType sourceType,
        TimecodeGenerationJobMode mode = TimecodeGenerationJobMode.TIMECODES,
        string? modelOverride = null,
        TimecodeTriggerSource triggerSource = TimecodeTriggerSource.MANUAL,
        Guid? materialId = null)
    {
        if (videoAssetId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(videoAssetId));

        if (assetVersion == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(assetVersion));

        if (requestedByUserId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(requestedByUserId));

        string? normalizedModelOverride = string.IsNullOrWhiteSpace(modelOverride)
            ? null
            : modelOverride.Trim();
        if (normalizedModelOverride?.Length > AiModelSlot.MAX_MODEL_LENGTH)
            return GeneralErrors.ValueIsInvalid(nameof(modelOverride));

        return new TimecodeGenerationJob(
            Guid.CreateVersion7(),
            videoAssetId,
            assetVersion,
            requestedByUserId,
            sourceType,
            mode,
            normalizedModelOverride,
            triggerSource,
            materialId == Guid.Empty ? null : materialId);
    }

    public void Start()
    {
        Status = TimecodeGenerationStatus.Processing;
        Stage = TimecodeGenerationStage.SourceFetch;
        ProgressPercent = 5;
        StartedAt ??= DateTime.UtcNow;
        ErrorCode = null;
        ErrorMessage = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateStage(TimecodeGenerationStage stage, int progressPercent)
    {
        Stage = stage;
        ProgressPercent = Math.Clamp(progressPercent, 0, 100);
        Status = TimecodeGenerationStatus.Processing;
        StartedAt ??= DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkCompleted()
    {
        Status = TimecodeGenerationStatus.Completed;
        Stage = TimecodeGenerationStage.Save;
        ProgressPercent = 100;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = CompletedAt.Value;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void MarkFailed(Error error)
    {
        Status = TimecodeGenerationStatus.Failed;
        // error.GetMessage() возвращает C# record-default ToString
        // (`ErrorMessage { Code = ..., Message = ..., InvalidField = ... }`) — это
        // diagnostic-форма, не для UI. Берём только .Message — фронт показывает её
        // напрямую в error-banner.
        ErrorCode = error.Messages.Count > 0 ? error.Messages[0].Code : null;
        ErrorMessage = error.Messages.Count > 0 ? error.Messages[0].Message : null;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = CompletedAt.Value;
    }
}
