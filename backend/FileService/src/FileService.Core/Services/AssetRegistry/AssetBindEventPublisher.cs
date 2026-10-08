using FileService.Core.Database;
using FileService.Core.Repositories;
using FileService.Domain;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Services.AssetRegistry;

public sealed class AssetBindEventPublisher(
    IOutboxService outbox,
    IMediaAssetRepository repository)
{
    public async Task<Result<long, Error>> PublishAsync(
        MediaAsset asset,
        TargetEntity targetEntity,
        bool advanceBindingRevision = false,
        bool requiresAuthoritativeConfirmation = false,
        bool publishReadyForPreviouslyConfirmedBinding = false,
        Guid? selectionId = null,
        CancellationToken cancellationToken = default)
    {
        if (AssetUsagePolicyCatalog.IsSingleAssetPerEntity(asset.UsageType) &&
            advanceBindingRevision)
        {
            long revision = await repository.GetNextBindingRevisionAsync(cancellationToken);
            UnitResult<Error> assignRevision = asset.AssignBindingRevision(revision, selectionId);
            if (assignRevision.IsFailure)
                return assignRevision.Error;
        }

        await outbox.PublishAsync(new FileAssetBound(
            asset.Id,
            asset.Kind.ToString().ToLowerInvariant(),
            asset.UsageType.ToApiString(),
            targetEntity.Id,
            targetEntity.Type,
            asset.BindingRevision,
            requiresAuthoritativeConfirmation));

        if (asset.Kind == AssetKind.VIDEO &&
            asset.Status == AssetStatus.READY &&
            asset.UsageType == AssetUsageType.MATERIAL_VIDEO &&
            (asset.ConfirmedBindingRevision == asset.BindingRevision ||
             publishReadyForPreviouslyConfirmedBinding &&
             asset.ConfirmedBindingRevision > asset.DetachedThroughBindingRevision))
        {
            await outbox.PublishAsync(new VideoReadyForProcessing(
                asset.Id,
                asset.Version,
                asset.UsageType.ToApiString(),
                targetEntity.Id,
                targetEntity.Type,
                asset.UploadedByUserId));
        }

        return asset.BindingRevision;
    }
}
