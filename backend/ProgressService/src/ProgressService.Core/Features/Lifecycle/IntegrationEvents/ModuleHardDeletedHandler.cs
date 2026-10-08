using ProgressService.Core.Abstractions;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace ProgressService.Core.Features.Lifecycle.IntegrationEvents;

public sealed class ModuleHardDeletedHandler
{
    private readonly IModuleProgressRepository _moduleProgressRepository;
    private readonly IModuleItemProgressRepository _moduleItemProgressRepository;
    private readonly IMaterialBookmarkRepository _bookmarkRepository;
    private readonly ILogger<ModuleHardDeletedHandler> _logger;

    public ModuleHardDeletedHandler(
        IModuleProgressRepository moduleProgressRepository,
        IModuleItemProgressRepository moduleItemProgressRepository,
        IMaterialBookmarkRepository bookmarkRepository,
        ILogger<ModuleHardDeletedHandler> logger)
    {
        _moduleProgressRepository = moduleProgressRepository;
        _moduleItemProgressRepository = moduleItemProgressRepository;
        _bookmarkRepository = bookmarkRepository;
        _logger = logger;
    }

    public async Task Handle(ModuleHardDeleted message, CancellationToken cancellationToken)
    {
        int moduleProgress = await _moduleProgressRepository.DeleteByModuleIdAsync(
            message.ModuleId, cancellationToken);

        int moduleItems = await _moduleItemProgressRepository.DeleteByModuleIdAsync(
            message.ModuleId, cancellationToken);

        // material_views (user-scoped) не трогаем на module-delete — если сам материал
        // удаляется, прилетит отдельный `material.hard_deleted`. Если нет — юзер-скоуп
        // view остаётся валиден (материал переехал в другой модуль/курс).
        Guid[] allTargetIds = [message.ModuleId, .. message.MaterialIds];
        int bookmarks = await _bookmarkRepository.DeleteByTargetEntityIdsAsync(
            allTargetIds, cancellationToken);

        _logger.LogInformation(
            "ModuleHardDeleted {ModuleId}: deleted {ModuleProgress} module progress, {ModuleItems} module items, {Bookmarks} bookmarks",
            message.ModuleId,
            moduleProgress,
            moduleItems,
            bookmarks);
    }
}
