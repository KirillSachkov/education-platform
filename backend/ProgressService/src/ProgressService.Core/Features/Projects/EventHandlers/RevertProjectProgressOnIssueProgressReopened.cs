using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using ProgressService.Domain.Projects;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Projects.EventHandlers;

/// <summary>
/// Декрементирует счётчик завершённых задач в ProjectProgress при reopen ревью.
/// Если проект был COMPLETED — статус откатывается до IN_PROGRESS. Идемпотентно: если
/// ProjectProgress отсутствует (например, seeded data без вызова StartProject) — no-op.
/// </summary>
public sealed class RevertProjectProgressOnIssueProgressReopened
    : IDomainEventHandler<IssueProgressReopenedEvent>
{
    private readonly IProjectProgressRepository _projectProgressRepository;

    public RevertProjectProgressOnIssueProgressReopened(IProjectProgressRepository projectProgressRepository)
    {
        _projectProgressRepository = projectProgressRepository;
    }

    public async Task<UnitResult<Error>> Handle(IssueProgressReopenedEvent domainEvent, CancellationToken ct)
    {
        IssueProgress issueProgress = domainEvent.Progress;

        Result<ProjectProgress, Error> projectProgressResult = await _projectProgressRepository
            .GetByAsync(
                x => x.EnrollmentId == issueProgress.EnrollmentId && x.ProjectId == issueProgress.ProjectId,
                ct);
        if (projectProgressResult.IsNotFound())
        {
            return UnitResult.Success<Error>();
        }
        if (projectProgressResult.IsFailure)
        {
            return projectProgressResult.Error;
        }

        return projectProgressResult.Value.RegisterIssueUncompleted();
    }
}
