using ProgressService.Core.Abstractions;
using ProgressService.Domain.Gamification;
using ProgressService.Domain.Projects;
using ProgressService.Domain.Projects.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Gamification.EventHandlers;

/// <summary>
/// Начисляет XP за завершение проекта.
/// </summary>
public sealed class AwardXpOnProjectCompleted : IDomainEventHandler<ProjectProgressCompletedEvent>
{
    private readonly IXpAwardService _xpAwardService;

    public AwardXpOnProjectCompleted(IXpAwardService xpAwardService)
    {
        _xpAwardService = xpAwardService;
    }

    /// <summary>
    /// Преобразует событие завершения проекта в запрос на начисление XP.
    /// </summary>
    public async Task<UnitResult<Error>> Handle(ProjectProgressCompletedEvent domainEvent, CancellationToken cancellationToken)
    {
        ProjectProgress projectProgress = domainEvent.Progress;

        return await _xpAwardService.AwardAsync(
            new XpAwardCommand(
                UserId: Guid.Empty,
                EnrollmentId: projectProgress.EnrollmentId,
                AwardType: XpAwardType.PROJECT_COMPLETED,
                SourceId: projectProgress.Id),
            cancellationToken);
    }
}
