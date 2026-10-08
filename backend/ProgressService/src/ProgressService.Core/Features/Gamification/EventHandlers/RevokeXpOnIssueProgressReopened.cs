using ProgressService.Core.Abstractions;
using ProgressService.Domain.Gamification;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Gamification.EventHandlers;

/// <summary>
/// Отзывает XP-награду <see cref="XpAwardType.ISSUE_APPROVED"/> при reopen ревью задачи.
/// </summary>
public sealed class RevokeXpOnIssueProgressReopened : IDomainEventHandler<IssueProgressReopenedEvent>
{
    private readonly IXpAwardService _xpAwardService;

    public RevokeXpOnIssueProgressReopened(IXpAwardService xpAwardService)
    {
        _xpAwardService = xpAwardService;
    }

    public async Task<UnitResult<Error>> Handle(IssueProgressReopenedEvent domainEvent, CancellationToken cancellationToken)
    {
        IssueProgress issueProgress = domainEvent.Progress;

        return await _xpAwardService.RevokeAsync(
            new XpAwardCommand(
                UserId: Guid.Empty,
                EnrollmentId: issueProgress.EnrollmentId,
                AwardType: XpAwardType.ISSUE_APPROVED,
                SourceId: issueProgress.Id),
            cancellationToken);
    }
}
