using Core.Database;
using FileService.Core.Database;
using FileService.Core.Repositories;
using FileService.Domain;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Features.AssetRegistry.IntegrationEvents;

public sealed class FileAssetBindingConfirmedHandler
{
    private readonly IMediaAssetRepository _repository;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactionManager;

    public FileAssetBindingConfirmedHandler(
        IMediaAssetRepository repository,
        IOutboxService outbox,
        ITransactionManager transactionManager)
    {
        _repository = repository;
        _outbox = outbox;
        _transactionManager = transactionManager;
    }

    public async Task Handle(FileAssetBindingConfirmed message, CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> result = await _repository.GetByAsync(
            asset => asset.Id == message.AssetId,
            cancellationToken);
        if (result.IsFailure)
            return;

        MediaAsset asset = result.Value;
        if (message.BindingRevision > asset.BindingRevision)
            return;

        // Delete can overtake Bound/Confirmation on parallel consumers. Re-emitting
        // the revision-scoped deletion after the authoritative confirmation makes
        // downstream aggregates converge instead of retaining a dangling media id.
        if (asset.Status is AssetStatus.DELETING or AssetStatus.DELETED)
        {
            if (message.BindingRevision <= asset.ConfirmedBindingRevision)
                return;

            UnitResult<Error> deletedConfirmation = asset.ConfirmBindingRevision(message.BindingRevision);
            if (deletedConfirmation.IsFailure)
                return;

            await _outbox.PublishAsync(new FileAssetDeleted(
                asset.Id,
                asset.Kind.ToString().ToLowerInvariant(),
                asset.UsageType.ToApiString(),
                asset.TargetEntity?.Id,
                asset.TargetEntity?.Type,
                message.BindingRevision));

            UnitResult<Error> deletedSave = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (deletedSave.IsFailure)
                throw deletedSave.Error.AsTransient().ToException();
            return;
        }

        if (message.BindingRevision <= asset.ConfirmedBindingRevision)
            return;

        UnitResult<Error> confirmation = asset.ConfirmBindingRevision(message.BindingRevision);
        if (confirmation.IsFailure)
            return;

        UnitResult<Error> save = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
            throw save.Error.AsTransient().ToException();
    }
}
