using CSharpFunctionalExtensions;
using MaterialProcessingService.Domain.AiSettings;
using MaterialProcessingService.Domain.Common.ValueObjects;
using SharedKernel;

namespace MaterialProcessingService.Domain.ContentDrafts;

public sealed class ContentGenerationJob
{
    private ContentGenerationJob()
    {
    }

    private ContentGenerationJob(
        Guid id,
        Guid videoAssetId,
        Guid materialId,
        Guid assetVersion,
        Guid requestedByUserId,
        ProcessingSourceType sourceType,
        string? modelOverride)
    {
        Id = id;
        VideoAssetId = videoAssetId;
        MaterialId = materialId;
        AssetVersion = assetVersion;
        RequestedByUserId = requestedByUserId;
        SourceType = sourceType;
        ModelOverride = modelOverride;
        Status = ContentGenerationStatus.Queued;
        Stage = ContentGenerationStage.Queued;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public Guid VideoAssetId { get; private set; }

    public Guid MaterialId { get; private set; }

    public Guid AssetVersion { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public ContentGenerationStatus Status { get; private set; }

    public ContentGenerationStage Stage { get; private set; }

    public int ProgressPercent { get; private set; }

    public ProcessingSourceType SourceType { get; private set; } = null!;

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    ///     Optional admin-provided model override (например "openai/gpt-4.1-nano")
    ///     для A/B-тестирования на dev. См. описание на TimecodeGenerationJob.
    /// </summary>
    public string? ModelOverride { get; private set; }

    public static Result<ContentGenerationJob, Error> Create(
        Guid videoAssetId,
        Guid materialId,
        Guid assetVersion,
        Guid requestedByUserId,
        ProcessingSourceType sourceType,
        string? modelOverride = null)
    {
        if (videoAssetId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(videoAssetId));

        if (materialId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(materialId));

        if (assetVersion == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(assetVersion));

        if (requestedByUserId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(requestedByUserId));

        string? normalizedModelOverride = string.IsNullOrWhiteSpace(modelOverride)
            ? null
            : modelOverride.Trim();
        if (normalizedModelOverride?.Length > AiModelSlot.MAX_MODEL_LENGTH)
            return GeneralErrors.ValueIsInvalid(nameof(modelOverride));

        return new ContentGenerationJob(
            Guid.CreateVersion7(),
            videoAssetId,
            materialId,
            assetVersion,
            requestedByUserId,
            sourceType,
            normalizedModelOverride);
    }

    public void Start()
    {
        Status = ContentGenerationStatus.Processing;
        Stage = ContentGenerationStage.SourceFetch;
        ProgressPercent = 5;
        StartedAt ??= DateTime.UtcNow;
        ErrorCode = null;
        ErrorMessage = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateStage(ContentGenerationStage stage, int progressPercent)
    {
        Stage = stage;
        ProgressPercent = Math.Clamp(progressPercent, 0, 100);
        Status = ContentGenerationStatus.Processing;
        StartedAt ??= DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkCompleted()
    {
        Status = ContentGenerationStatus.Completed;
        Stage = ContentGenerationStage.Save;
        ProgressPercent = 100;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = CompletedAt.Value;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void MarkFailed(Error error)
    {
        Status = ContentGenerationStatus.Failed;
        // error.GetMessage() возвращает C# record-default ToString — diagnostic, не UI.
        // Берём только .Message — фронт показывает её напрямую в error-banner.
        ErrorCode = error.Messages.Count > 0 ? error.Messages[0].Code : null;
        ErrorMessage = error.Messages.Count > 0 ? error.Messages[0].Message : null;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = CompletedAt.Value;
    }
}
