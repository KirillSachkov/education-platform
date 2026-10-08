using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using Microsoft.Extensions.Caching.Hybrid;
using Shared.Messaging.IntegrationEvents.Education.Events;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace EducationContentService.Core.Features.FileEvents;

public sealed class MaterialVideoDeletedHandler
{
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly ILogger<MaterialVideoDeletedHandler> _logger;

    public MaterialVideoDeletedHandler(
        IMaterialsRepository materialsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        ILogger<MaterialVideoDeletedHandler> logger)
    {
        _materialsRepository = materialsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _cache = cache;
        _logger = logger;
    }

    public async Task Handle(FileAssetDeleted message, CancellationToken ct)
    {
        if (!string.Equals(message.TargetEntityType, FileEventsRouting.EntityTypes.MATERIAL, StringComparison.Ordinal)
            || !string.Equals(message.UsageType, FileEventsRouting.UsageTypes.MATERIAL_VIDEO, StringComparison.Ordinal))
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

        if (materialResult.Value.VideoId?.Value != message.AssetId ||
            materialResult.Value.VideoBindingRevision != message.BindingRevision)
            return;

        Material material = materialResult.Value;
        List<Guid> courseIds = await _materialsRepository.GetCourseIdsAsync(material.Id, ct);
        bool isLastPublishedPayload = material.Status == PublicationStatus.PUBLISHED && material.Content is null;
        if (isLastPublishedPayload)
        {
            UnitResult<Error> draft = material.SendToDraft();
            if (draft.IsFailure)
                throw draft.Error.AsTransient().ToException();

            await _outbox.PublishAsync(new MaterialSentToDraft(material.Id));
        }

        material.DetachVideo();
        UnitResult<Error> save = await _transactionManager.SaveChangesAsync(ct);
        if (save.IsFailure)
            throw save.Error.AsTransient().ToException();

        try
        {
            foreach (Guid courseId in courseIds)
                await CourseCacheInvalidator.InvalidateAsync(_cache, courseId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Course cache invalidation failed after deleting material video {AssetId} (will expire via TTL)",
                message.AssetId);
        }
    }
}
