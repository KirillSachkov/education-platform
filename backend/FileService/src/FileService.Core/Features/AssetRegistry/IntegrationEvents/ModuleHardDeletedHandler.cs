using FileService.Core.Services.AssetRegistry;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace FileService.Core.Features.AssetRegistry.IntegrationEvents;

/// <summary>
///     Обрабатывает событие ModuleHardDeleted — удаляет все медиа-файлы модуля и его материалов.
///     После унификации Lesson+Article → Material в ECS поле <c>MaterialIds</c> хранит ID материалов.
/// </summary>
public sealed class ModuleHardDeletedHandler
{
    private readonly AssetDeletionLifecycleService _deletionService;
    private readonly ILogger<ModuleHardDeletedHandler> _logger;

    public ModuleHardDeletedHandler(
        ILogger<ModuleHardDeletedHandler> logger,
        AssetDeletionLifecycleService deletionService)
    {
        _logger = logger;
        _deletionService = deletionService;
    }

    public async Task Handle(ModuleHardDeleted message, CancellationToken cancellationToken)
    {
        List<(string EntityType, Guid EntityId)> entities =
            [("module", message.ModuleId)];

        foreach (Guid materialId in message.MaterialIds)
        {
            entities.Add(("material", materialId));
        }

        int totalDeleted = await _deletionService.DeleteByTargetEntitiesBatchAsync(entities, cancellationToken);

        _logger.LogInformation(
            "ModuleHardDeleted: Deleted {TotalDeleted} file(s) for module {ModuleId} ({MaterialCount} materials)",
            totalDeleted,
            message.ModuleId,
            message.MaterialIds.Count);
    }
}
