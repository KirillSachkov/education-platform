using ProgressService.Core.Abstractions;
using ProgressService.Domain.Gamification;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Gamification.EventHandlers;

/// <summary>
/// Начисляет XP за завершённую и одобренную задачу.
/// </summary>
public sealed class AwardXpOnIssueApproved : IDomainEventHandler<IssueProgressApprovedEvent>
{
    private readonly IXpAwardService _xpAwardService;

    public AwardXpOnIssueApproved(IXpAwardService xpAwardService)
    {
        _xpAwardService = xpAwardService;
    }

    /// <summary>
    /// Преобразует событие завершения задачи в запрос на начисление XP.
    /// </summary>
    public async Task<UnitResult<Error>> Handle(IssueProgressApprovedEvent domainEvent, CancellationToken cancellationToken)
    {
        IssueProgress issueProgress = domainEvent.Progress;

        // UserId резолвится сервисом по EnrollmentId.
        return await _xpAwardService.AwardAsync(
            new XpAwardCommand(
                UserId: Guid.Empty,
                EnrollmentId: issueProgress.EnrollmentId,
                AwardType: XpAwardType.ISSUE_APPROVED,
                SourceId: issueProgress.Id),
            cancellationToken);
    }
}
