using Core.Database;
using FileService.Core.Database;
using FileService.Core.Repositories;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Features.AssetRegistry.IntegrationEvents;

public sealed class FileAssetDetachedHandler
{
    private readonly IMediaAssetRepository _repository;
    private readonly IAssetSlotReplacement _deletionPublisher;
    private readonly ITransactionManager _transactionManager;

    public FileAssetDetachedHandler(
        IMediaAssetRepository repository,
        IAssetSlotReplacement deletionPublisher,
        ITransactionManager transactionManager)
    {
        _repository = repository;
        _deletionPublisher = deletionPublisher;
        _transactionManager = transactionManager;
    }

    public async Task Handle(FileAssetDetached message, CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> result = await _repository.GetByAsync(
            asset => asset.Id == message.AssetId,
            cancellationToken);
        if (result.IsFailure ||
            result.Value.Status is AssetStatus.DELETING or AssetStatus.DELETED ||
            message.ExpectedBindingRevision > result.Value.BindingRevision)
            return;

        MediaAsset asset = result.Value;
        UnitResult<Error> detached = asset.DetachThroughBindingRevision(message.ExpectedBindingRevision);
        if (detached.IsFailure)
            return;

        // A later prepared attempt may still be confirmed. Keep the physical asset
        // until that attempt resolves; the watermark already makes the older
        // authoritative binding inactive for reads and processing.
        if (message.ExpectedBindingRevision == asset.BindingRevision)
            await _deletionPublisher.RequestDeleteAsync(asset);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            throw saveResult.Error.AsTransient().ToException();
    }
}
