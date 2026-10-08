using Core.Database;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Domain.Materials;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace EducationContentService.Core.Features.FileEvents;

public sealed class MaterialPreviewDeletedHandler
{
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<MaterialPreviewDeletedHandler> _logger;

    public MaterialPreviewDeletedHandler(
        IMaterialsRepository materialsRepository,
        ITransactionManager transactionManager,
        ILogger<MaterialPreviewDeletedHandler> logger)
    {
        _materialsRepository = materialsRepository;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task Handle(FileAssetDeleted message, CancellationToken ct)
    {
        if (!string.Equals(message.TargetEntityType, FileEventsRouting.EntityTypes.MATERIAL, StringComparison.Ordinal)
            || !string.Equals(message.UsageType, FileEventsRouting.UsageTypes.MATERIAL_PREVIEW, StringComparison.Ordinal))
            return;

        if (message.TargetEntityId is null)
            return;

        Result<Material, Error> materialResult = await _materialsRepository
            .GetByAsync(m => m.Id == message.TargetEntityId.Value, ct);
        if (materialResult.IsFailure)
        {
            _logger.LogInformation(
                "Material {MaterialId} not found for FileAssetDeleted {AssetId} — late event, no-op",
                message.TargetEntityId, message.AssetId);
            return;
        }

        if (materialResult.Value.ImageId?.Value != message.AssetId ||
            materialResult.Value.ImageBindingRevision != message.BindingRevision)
            return;

        materialResult.Value.DetachImage();
        UnitResult<Error> save = await _transactionManager.SaveChangesAsync(ct);
        if (save.IsFailure)
            throw save.Error.AsTransient().ToException();
    }
}
