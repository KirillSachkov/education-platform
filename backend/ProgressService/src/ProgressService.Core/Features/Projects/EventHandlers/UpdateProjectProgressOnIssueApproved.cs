using ProgressService.Core.Abstractions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using ProgressService.Domain.Projects;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Projects.EventHandlers;

/// <summary>
/// Обновляет прогресс проекта при одобрении задачи.
/// Внутрипроцессный эффект: запись в ту же БД в рамках той же транзакции.
/// </summary>
public sealed class UpdateProjectProgressOnIssueApproved
    : IDomainEventHandler<IssueProgressApprovedEvent>
{
    private readonly IProjectProgressRepository _projectProgressRepository;

    public UpdateProjectProgressOnIssueApproved(IProjectProgressRepository projectProgressRepository)
    {
        _projectProgressRepository = projectProgressRepository;
    }

    public async Task<UnitResult<Error>> Handle(IssueProgressApprovedEvent domainEvent, CancellationToken ct)
    {
        IssueProgress issueProgress = domainEvent.Progress;

        Result<ProjectProgress, Error> projectProgressResult = await _projectProgressRepository
            .GetByAsync(
                x => x.EnrollmentId == issueProgress.EnrollmentId && x.ProjectId == issueProgress.ProjectId,
                ct);
        if (projectProgressResult.IsFailure)
        {
            return projectProgressResult.Error;
        }

        ProjectProgress projectProgress = projectProgressResult.Value;

        UnitResult<Error> registerIssueCompletedResult = projectProgress.RegisterIssueCompleted();
        if (registerIssueCompletedResult.IsFailure)
        {
            return registerIssueCompletedResult.Error;
        }

        UnitResult<Error> tryCompleteProjectResult = projectProgress.TryCompleteProject();
        if (tryCompleteProjectResult.IsFailure)
        {
            return tryCompleteProjectResult.Error;
        }

        return UnitResult.Success<Error>();
    }
}
