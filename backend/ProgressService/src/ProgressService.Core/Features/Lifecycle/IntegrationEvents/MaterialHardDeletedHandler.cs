using ProgressService.Core.Abstractions;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace ProgressService.Core.Features.Lifecycle.IntegrationEvents;

/// <remarks>
///     Попытки квизов НЕ каскадятся (ST-13 #493): квиз — standalone-сущность, переиспользуемая
///     несколькими материалами; его попытки чистит <see cref="QuizHardDeletedHandler"/> на
///     <c>quiz.hard_deleted</c>.
/// </remarks>
public sealed class MaterialHardDeletedHandler
{
    private readonly IMaterialViewRepository _materialViewRepository;
    private readonly IAnonymousMaterialViewRepository _anonymousMaterialViewRepository;
    private readonly IModuleItemProgressRepository _moduleItemProgressRepository;
    private readonly IMaterialBookmarkRepository _bookmarkRepository;
    private readonly IMaterialNoteRepository _materialNoteRepository;
    private readonly ILogger<MaterialHardDeletedHandler> _logger;

    public MaterialHardDeletedHandler(
        IMaterialViewRepository materialViewRepository,
        IAnonymousMaterialViewRepository anonymousMaterialViewRepository,
        IModuleItemProgressRepository moduleItemProgressRepository,
        IMaterialBookmarkRepository bookmarkRepository,
        IMaterialNoteRepository materialNoteRepository,
        ILogger<MaterialHardDeletedHandler> logger)
    {
        _materialViewRepository = materialViewRepository;
        _anonymousMaterialViewRepository = anonymousMaterialViewRepository;
        _moduleItemProgressRepository = moduleItemProgressRepository;
        _bookmarkRepository = bookmarkRepository;
        _materialNoteRepository = materialNoteRepository;
        _logger = logger;
    }

    public async Task Handle(MaterialHardDeleted message, CancellationToken cancellationToken)
    {
        int materialViews = await _materialViewRepository.DeleteByMaterialIdAsync(
            message.MaterialId, cancellationToken);

        int anonymousViews = await _anonymousMaterialViewRepository.DeleteByMaterialIdAsync(
            message.MaterialId, cancellationToken);

        int moduleItems = await _moduleItemProgressRepository.DeleteByReferenceIdAsync(
            message.MaterialId, cancellationToken);

        int bookmarks = await _bookmarkRepository.DeleteByTargetEntityIdsAsync(
            [message.MaterialId], cancellationToken);

        int notes = await _materialNoteRepository.DeleteByMaterialIdAsync(
            message.MaterialId, cancellationToken);

        _logger.LogInformation(
            "MaterialHardDeleted {MaterialId}: deleted {MaterialViews} material views, {AnonymousViews} anonymous views, {ModuleItems} module items, {Bookmarks} bookmarks, {Notes} notes",
            message.MaterialId,
            materialViews,
            anonymousViews,
            moduleItems,
            bookmarks,
            notes);
    }
}
