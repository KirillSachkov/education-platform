namespace FileService.Domain;

public sealed class MediaAsset
{
    private List<ImageVariant> _imageVariants = [];

    private MediaAsset()
    {
    }

    private MediaAsset(
        Guid id,
        AssetKind kind,
        AssetUsageType usageType,
        FileName fileName,
        MediaContentType contentType,
        long size,
        TargetEntity? targetEntity,
        Guid? draftId,
        bool isTemporary,
        Guid? uploadedByUserId)
    {
        Id = id;
        Kind = kind;
        UsageType = usageType;
        Status = AssetStatus.PENDING_UPLOAD;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        TargetEntity = targetEntity;
        DraftId = draftId;
        IsTemporary = isTemporary;
        UploadedByUserId = uploadedByUserId;
        CreatedAt = DateTime.UtcNow;
        Version = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public AssetKind Kind { get; private set; }

    public AssetUsageType UsageType { get; private set; }

    public AssetStatus Status { get; private set; }

    public FileName FileName { get; private set; }

    public MediaContentType ContentType { get; private set; }

    public long Size { get; private set; }

    public TargetEntity? TargetEntity { get; private set; }

    public Guid? DraftId { get; private set; }

    public bool IsTemporary { get; private set; }

    public Guid? UploadedByUserId { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime? BoundAt { get; private set; }

    /// <summary>
    ///     Monotonic FileService-wide revision of the latest logical binding.
    ///     Zero denotes a binding created before revision tracking was introduced.
    /// </summary>
    public long BindingRevision { get; private set; }

    public Guid? BindingSelectionId { get; private set; }

    public long ConfirmedBindingRevision { get; private set; } = -1;

    /// <summary>
    ///     Greatest authoritative revision that has been detached. A confirmed
    ///     binding is active only while its revision is greater than this watermark.
    ///     The -1 sentinel distinguishes a new unconfirmed binding from legacy rev0.
    /// </summary>
    public long DetachedThroughBindingRevision { get; private set; } = -1;

    public DateTime? LastVerifiedAt { get; private set; }
    public DateTime? ProcessingStartedAt { get; private set; }
    public DateTime? DeleteRequestedAt { get; private set; }

    public DateTime? DeletedAt { get; private set; }

    /// <summary>
    ///     Server-generated responsive WebP variants of an image asset (issue #646).
    ///     Persisted as a JSONB blob (<c>image_variants</c> column) — additive, never
    ///     replaces the original object. Empty for non-images and not-yet-processed images.
    /// </summary>
    public IReadOnlyList<ImageVariant> ImageVariants => _imageVariants;

    public Guid Version { get; private set; }

    public static Result<MediaAsset, Error> Register(
        Guid id,
        AssetKind kind,
        AssetUsageType usageType,
        FileName fileName,
        MediaContentType contentType,
        long size,
        Guid? draftId,
        TargetEntity? targetEntity,
        bool isTemporary,
        Guid? uploadedByUserId = null)
    {
        Result<AssetUsagePolicy, Error> policyResult = AssetUsagePolicyCatalog.Get(usageType);
        if (policyResult.IsFailure)
        {
            return policyResult.Error;
        }

        AssetUsagePolicy policy = policyResult.Value;
        if (policy.Kind != kind)
        {
            return GeneralErrors.ValueIsInvalid("kind");
        }

        UnitResult<Error> uploadValidation = policy.ValidateUpload(fileName, contentType, size);
        if (uploadValidation.IsFailure)
        {
            return uploadValidation.Error;
        }

        UnitResult<Error> matrixValidation = ValidateRegistrationMatrix(policy, draftId, targetEntity, isTemporary);
        if (matrixValidation.IsFailure)
        {
            return matrixValidation.Error;
        }

        return new MediaAsset(
            id == Guid.Empty ? Guid.CreateVersion7() : id,
            kind,
            usageType,
            fileName,
            contentType,
            size,
            targetEntity,
            draftId,
            isTemporary,
            uploadedByUserId);
    }

    public static Result<MediaAsset, Error> RegisterFromProvider(
        Guid id,
        AssetUsageType usageType,
        FileName fileName,
        MediaContentType contentType,
        TargetEntity? targetEntity,
        AssetStatus initialStatus,
        Guid? draftId = null,
        Guid? uploadedByUserId = null)
    {
        Result<AssetUsagePolicy, Error> policyResult = AssetUsagePolicyCatalog.Get(usageType);
        if (policyResult.IsFailure)
        {
            return policyResult.Error;
        }

        AssetUsagePolicy policy = policyResult.Value;
        if (policy.Kind != AssetKind.VIDEO)
        {
            return GeneralErrors.ValueIsInvalid("kind");
        }

        if (policy.RegistrationMode is not AssetRegistrationMode.EntityRequired
            and not AssetRegistrationMode.DraftOrEntity)
        {
            return Error.Validation("asset.binding.invalid", "Регистрация провайдера требует привязки к сущности или черновику");
        }

        bool hasDraft = draftId.HasValue && draftId.Value != Guid.Empty;
        bool hasTarget = targetEntity is not null;

        if (!hasDraft && !hasTarget)
        {
            return GeneralErrors.ValueIsRequired("targetEntity");
        }

        if (hasDraft && hasTarget)
        {
            return Error.Validation(
                "asset.binding.invalid",
                "Ресурс не может быть привязан одновременно к черновику и сущности");
        }

        if (hasTarget && !policy.IsTargetTypeAllowed(targetEntity!.Type))
        {
            return Error.Validation("targetEntity.type.invalid", "Тип целевой сущности не допускается для этого ресурса");
        }

        if (initialStatus is not AssetStatus.READY and not AssetStatus.PROCESSING)
        {
            return Error.Validation("asset.status.invalid", "Начальный статус должен быть Ready или Processing");
        }

        bool isTemporary = hasDraft;

        var asset = new MediaAsset(
            id == Guid.Empty ? Guid.CreateVersion7() : id,
            AssetKind.VIDEO,
            usageType,
            fileName,
            contentType,
            0,
            targetEntity,
            draftId,
            isTemporary,
            uploadedByUserId);

        UnitResult<Error> transition = initialStatus == AssetStatus.READY
            ? asset.MarkReady()
            : asset.MarkProcessing();

        if (transition.IsFailure)
        {
            return transition.Error;
        }

        return asset;
    }

    public UnitResult<Error> MarkProcessing()
    {
        if (Status == AssetStatus.PROCESSING)
        {
            return UnitResult.Success<Error>();
        }

        if (Status != AssetStatus.PENDING_UPLOAD)
        {
            return Error.Validation("asset.status.invalid", "Ресурс не может перейти в обработку из текущего статуса");
        }

        Status = AssetStatus.PROCESSING;
        ProcessingStartedAt ??= DateTime.UtcNow;
        FailureReason = null;
        TouchVersion();
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> MarkReady()
    {
        if (Status == AssetStatus.READY)
        {
            return UnitResult.Success<Error>();
        }

        if (Status is not AssetStatus.PENDING_UPLOAD and not AssetStatus.PROCESSING)
        {
            return Error.Validation("asset.status.invalid", "Ресурс не может перейти в готовность из текущего статуса");
        }

        Status = AssetStatus.READY;
        FailureReason = null;
        CompletedAt = DateTime.UtcNow;
        LastVerifiedAt = DateTime.UtcNow;
        TouchVersion();
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> MarkFailed(string reason)
    {
        if (Status == AssetStatus.FAILED)
        {
            return UnitResult.Success<Error>();
        }

        if (Status is not AssetStatus.PENDING_UPLOAD and not AssetStatus.PROCESSING)
        {
            return Error.Validation("asset.status.invalid", "Ресурс не может перейти в ошибку из текущего статуса");
        }

        Status = AssetStatus.FAILED;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Unknown failure" : reason.Trim();
        LastVerifiedAt = DateTime.UtcNow;
        TouchVersion();
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> BindTo(TargetEntity targetEntity)
    {
        // Files must be Ready. Videos are bindable while still Processing — the
        // Kinescope provider finishes asynchronously, and the target-entity link
        // must be established at save time so downstream services can pick it up.
        bool statusOk = Kind == AssetKind.VIDEO
            ? Status is AssetStatus.READY or AssetStatus.PROCESSING
            : Status == AssetStatus.READY;

        if (!statusOk)
        {
            return Error.Validation("asset.not.ready", "Привязать можно только готовые ресурсы");
        }

        if (DraftId is null || !IsTemporary)
        {
            return Error.Validation("asset.binding.invalid", "Привязать можно только временные черновые ресурсы");
        }

        Result<AssetUsagePolicy, Error> policyResult = AssetUsagePolicyCatalog.Get(UsageType);
        if (policyResult.IsFailure)
        {
            return policyResult.Error;
        }

        if (!policyResult.Value.IsTargetTypeAllowed(targetEntity.Type))
        {
            return Error.Validation("targetEntity.type.invalid", "Тип целевой сущности не допускается для этого ресурса");
        }

        // EF owned entities cannot be shared between multiple aggregate roots.
        // Batch bind handlers pass one validated value to many assets, so materialize
        // an owned copy per asset instead of re-parenting the same tracked instance.
        TargetEntity = TargetEntity.Of(targetEntity.Type, targetEntity.Id).Value;
        DraftId = null;
        IsTemporary = false;
        BoundAt = DateTime.UtcNow;
        TouchVersion();
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> AssignBindingRevision(long bindingRevision, Guid? selectionId = null)
    {
        if (bindingRevision <= BindingRevision)
            return Error.Validation("asset.binding.revision.invalid", "Ревизия привязки должна возрастать");

        BindingRevision = bindingRevision;
        BindingSelectionId = selectionId;
        BoundAt = DateTime.UtcNow;
        TouchVersion();
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> ConfirmBindingRevision(long bindingRevision)
    {
        if (bindingRevision > BindingRevision)
            return Error.Conflict("asset.binding.revision.future", "Ревизия привязки ещё не была подготовлена");

        if (bindingRevision <= ConfirmedBindingRevision)
            return UnitResult.Success<Error>();

        ConfirmedBindingRevision = bindingRevision;
        TouchVersion();
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> DetachThroughBindingRevision(long bindingRevision)
    {
        if (bindingRevision > BindingRevision)
            return Error.Conflict("asset.binding.revision.future", "Ревизия привязки ещё не была подготовлена");

        if (bindingRevision <= DetachedThroughBindingRevision)
            return UnitResult.Success<Error>();

        DetachedThroughBindingRevision = bindingRevision;
        TouchVersion();
        return UnitResult.Success<Error>();
    }

    public long GetDeletionBindingRevision() => Math.Max(ConfirmedBindingRevision, 0);

    public UnitResult<Error> RequestDelete()
    {
        if (Status is AssetStatus.DELETING or AssetStatus.DELETED)
        {
            return UnitResult.Success<Error>();
        }

        if (Status is not AssetStatus.PENDING_UPLOAD and not AssetStatus.PROCESSING and
            not AssetStatus.READY and not AssetStatus.FAILED)
        {
            return Error.Validation("asset.status.invalid", "Ресурс не может быть удалён из текущего статуса");
        }

        Status = AssetStatus.DELETING;
        DeleteRequestedAt = DateTime.UtcNow;
        TouchVersion();
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> ReassignOwner(Guid newOwnerId)
    {
        if (newOwnerId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(newOwnerId));
        }

        if (UploadedByUserId == newOwnerId)
        {
            return UnitResult.Success<Error>();
        }

        UploadedByUserId = newOwnerId;
        TouchVersion();

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> MarkDeleted()
    {
        if (Status == AssetStatus.DELETED)
        {
            return UnitResult.Success<Error>();
        }

        if (Status != AssetStatus.DELETING)
        {
            return Error.Validation("asset.status.invalid", "Ресурс должен быть в процессе удаления перед окончательным удалением");
        }

        Status = AssetStatus.DELETED;
        DeletedAt = DateTime.UtcNow;
        TouchVersion();
        return UnitResult.Success<Error>();
    }

    public bool IsContentReadable() => Status == AssetStatus.READY;

    /// <summary>
    ///     Whether this asset is a candidate for responsive variant generation:
    ///     a READY file whose content-type is an in-scope image (issue #646).
    /// </summary>
    public bool IsImageVariantEligible() =>
        Kind == AssetKind.FILE
        && Status == AssetStatus.READY
        && ImageVariantPolicy.IsImageContentType(ContentType.Value);

    public bool HasImageVariants() => _imageVariants.Count > 0;

    /// <summary>
    ///     Replaces the variant set wholesale. Idempotent: re-running variant
    ///     generation overwrites any prior set with the freshly-computed one.
    /// </summary>
    public void SetImageVariants(IEnumerable<ImageVariant> variants)
    {
        _imageVariants = variants
            .OrderBy(v => v.Width)
            .ToList();
        TouchVersion();
    }

    public void ClearImageVariants()
    {
        if (_imageVariants.Count == 0)
        {
            return;
        }

        _imageVariants = [];
        TouchVersion();
    }

    /// <summary>
    ///     Selects the variant storage key + content-type to serve for the requested
    ///     width, or <c>null</c> when the original should be served instead (issue #646).
    /// </summary>
    public ImageVariant? SelectVariantForWidth(int? requestedWidth) =>
        ImageVariantPolicy.SelectVariant(_imageVariants, requestedWidth);

    public bool IsTombstoneVisible() => Status == AssetStatus.DELETED;

    public bool IsStale(DateTime utcNow, AssetStaleWorkflowType workflowType) =>
        workflowType switch
        {
            AssetStaleWorkflowType.PendingFileUpload => Status == AssetStatus.PENDING_UPLOAD && Kind == AssetKind.FILE,
            AssetStaleWorkflowType.DraftFile => Status is AssetStatus.PENDING_UPLOAD or AssetStatus.READY &&
                                                Kind == AssetKind.FILE && DraftId != null && IsTemporary,
            AssetStaleWorkflowType.VideoUpload => Status == AssetStatus.PENDING_UPLOAD && Kind == AssetKind.VIDEO,
            AssetStaleWorkflowType.VideoProcessing => Status == AssetStatus.PROCESSING && Kind == AssetKind.VIDEO,
            AssetStaleWorkflowType.Delete => Status == AssetStatus.DELETING,
            _ => false,
        };

    private static UnitResult<Error> ValidateRegistrationMatrix(
        AssetUsagePolicy policy,
        Guid? draftId,
        TargetEntity? targetEntity,
        bool isTemporary)
    {
        bool hasDraft = draftId.HasValue && draftId.Value != Guid.Empty;
        bool hasTarget = targetEntity is not null;

        if (hasDraft && hasTarget)
        {
            return Error.Validation(
                "asset.binding.invalid",
                "Ресурс не может быть привязан одновременно к черновику и сущности");
        }

        return policy.RegistrationMode switch
        {
            AssetRegistrationMode.DraftRequired when !hasDraft || hasTarget || !isTemporary =>
                Error.Validation("asset.binding.invalid", "Некорректная конфигурация регистрации черновика"),
            AssetRegistrationMode.EntityRequired when hasDraft || !hasTarget || isTemporary =>
                Error.Validation("asset.binding.invalid", "Некорректная конфигурация регистрации сущности"),
            AssetRegistrationMode.DraftOrEntity when !hasDraft && !hasTarget =>
                Error.Validation("asset.binding.invalid", "Необходимо указать draftId или targetEntity"),
            AssetRegistrationMode.DraftOrEntity when hasDraft && hasTarget =>
                Error.Validation("asset.binding.invalid", "Нельзя указать одновременно draftId и targetEntity"),
            AssetRegistrationMode.DraftOrEntity when hasDraft && !isTemporary =>
                Error.Validation("asset.binding.invalid", "Черновой режим требует isTemporary=true"),
            AssetRegistrationMode.DraftOrEntity when hasTarget && isTemporary =>
                Error.Validation("asset.binding.invalid", "Режим сущности требует isTemporary=false"),
            _ => UnitResult.Success<Error>()
        };
    }

    private void TouchVersion() => Version = Guid.CreateVersion7();
}
