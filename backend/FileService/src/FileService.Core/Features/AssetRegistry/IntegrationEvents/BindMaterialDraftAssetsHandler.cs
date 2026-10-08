using Core.Database;
using FileService.Core.Database;
using FileService.Core.Repositories;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace FileService.Core.Features.AssetRegistry.IntegrationEvents;

public sealed class BindMaterialDraftAssetsHandler
{
    private readonly IMediaAssetRepository _repository;
    private readonly AssetBindEventPublisher _bindEventPublisher;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<BindMaterialDraftAssetsHandler> _logger;

    public BindMaterialDraftAssetsHandler(
        IMediaAssetRepository repository,
        AssetBindEventPublisher bindEventPublisher,
        ITransactionManager transactionManager,
        ILogger<BindMaterialDraftAssetsHandler> logger)
    {
        _repository = repository;
        _bindEventPublisher = bindEventPublisher;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task Handle(BindMaterialDraftAssets message, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(message.DraftId, out Guid draftGuid))
        {
            _logger.LogWarning(
                "BindMaterialDraftAssets: invalid DraftId '{DraftId}' for material {MaterialId}",
                message.DraftId,
                message.MaterialId);
            return;
        }

        List<MediaAsset> draftAssets = (await _repository.GetByDraftIdAsync(draftGuid, cancellationToken))
            .Where(asset => asset.Status is not AssetStatus.DELETED and not AssetStatus.DELETING)
            .ToList();

        if (draftAssets.Count == 0)
        {
            _logger.LogInformation(
                "BindMaterialDraftAssets: no draft assets found for DraftId {DraftId}, material {MaterialId}",
                message.DraftId,
                message.MaterialId);
            return;
        }

        var targetEntity = TargetEntity.Of("material", message.MaterialId);
        if (targetEntity.IsFailure)
        {
            _logger.LogWarning(
                "BindMaterialDraftAssets: failed to create TargetEntity for material {MaterialId}: {Error}",
                message.MaterialId,
                targetEntity.Error.GetMessage());
            return;
        }

        List<MediaAsset> handlerAssets = draftAssets
            .Where(asset => !AssetUsagePolicyCatalog.IsSingleAssetPerEntity(asset.UsageType))
            .ToList();

        if (handlerAssets.Count == 0)
        {
            _logger.LogInformation(
                "BindMaterialDraftAssets: draft {DraftId} contains only explicitly selected media",
                message.DraftId);
            return;
        }

        if (!message.ActorCanManageAnyAsset &&
            (message.ActorUserId is not { } actorUserId ||
             actorUserId == Guid.Empty ||
             handlerAssets.Any(asset => asset.UploadedByUserId != actorUserId)))
        {
            throw Error.Authorization(
                    "asset.draft.not.owner",
                    "Черновик содержит ресурсы другого пользователя")
                .ToException();
        }

        // PENDING_UPLOAD means the create event raced with /files/{id}/complete.
        // Failing transiently lets Wolverine retry instead of ACKing and orphaning
        // the draft forever. Slot replacement must not run before all pending files
        // become bindable.
        if (handlerAssets.Any(asset => asset.Status == AssetStatus.PENDING_UPLOAD))
        {
            throw Error.Failure(
                    "asset.draft.not.ready",
                    "Ресурсы черновика ещё не готовы к привязке")
                .AsTransient()
                .ToException();
        }

        // Preview/video are selected explicitly by the create request and sync-bound
        // before this event is emitted. Binding every historical single-slot draft here
        // could let an abandoned upload replace the asset the author selected.
        List<MediaAsset> bindable = handlerAssets
            .Where(a => a.Kind == AssetKind.VIDEO
                ? a.Status is AssetStatus.READY or AssetStatus.PROCESSING
                : a.Status == AssetStatus.READY)
            .ToList();

        foreach (MediaAsset skipped in handlerAssets.Except(bindable))
        {
            _logger.LogWarning(
                "BindMaterialDraftAssets: ignoring terminal asset {AssetId} (status={Status}, kind={Kind})",
                skipped.Id,
                skipped.Status,
                skipped.Kind);
        }

        int boundCount = 0;

        foreach (MediaAsset asset in bindable)
        {
            UnitResult<Error> bindResult = asset.BindTo(targetEntity.Value);
            if (bindResult.IsFailure)
            {
                throw bindResult.Error.ToException();
            }

            Result<long, Error> binding = await _bindEventPublisher.PublishAsync(
                asset,
                targetEntity.Value,
                advanceBindingRevision: false,
                cancellationToken: cancellationToken);
            if (binding.IsFailure)
                throw binding.Error.ToException();

            boundCount++;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "BindMaterialDraftAssets: failed to save changes for material {MaterialId}: {Error}",
                message.MaterialId,
                saveResult.Error.GetMessage());
            throw saveResult.Error.AsTransient().ToException();
        }

        _logger.LogInformation(
            "BindMaterialDraftAssets: bound {BoundCount}/{TotalCount} draft assets to material {MaterialId}",
            boundCount,
            draftAssets.Count,
            message.MaterialId);
    }

}
