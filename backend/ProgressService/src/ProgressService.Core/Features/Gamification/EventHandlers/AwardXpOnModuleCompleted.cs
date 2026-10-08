using ProgressService.Core.Abstractions;
using ProgressService.Domain.Gamification;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Modules.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Gamification.EventHandlers;

/// <summary>
/// Начисляет XP за завершение модуля.
/// </summary>
public sealed class AwardXpOnModuleCompleted : IDomainEventHandler<ModuleProgressCompletedEvent>
{
    private readonly IXpAwardService _xpAwardService;

    public AwardXpOnModuleCompleted(IXpAwardService xpAwardService)
    {
        _xpAwardService = xpAwardService;
    }

    /// <summary>
    /// Преобразует событие завершения модуля в запрос на начисление XP.
    /// </summary>
    public async Task<UnitResult<Error>> Handle(ModuleProgressCompletedEvent domainEvent, CancellationToken cancellationToken)
    {
        ModuleProgress moduleProgress = domainEvent.Progress;

        return await _xpAwardService.AwardAsync(
            new XpAwardCommand(
                UserId: Guid.Empty,
                EnrollmentId: moduleProgress.EnrollmentId,
                AwardType: XpAwardType.MODULE_COMPLETED,
                SourceId: moduleProgress.Id),
            cancellationToken);
    }
}
