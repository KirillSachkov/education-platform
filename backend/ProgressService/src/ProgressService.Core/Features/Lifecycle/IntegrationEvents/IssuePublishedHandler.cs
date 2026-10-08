using Core.Database;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Projects;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace ProgressService.Core.Features.Lifecycle.IntegrationEvents;

/// <summary>
///     Реакция на публикацию новой задачи в проекте: бампит TotalIssuesCount у всех
///     существующих ProjectProgress-строк проекта, чтобы invariant
///     <c>TotalIssuesCompleted &lt;= TotalIssuesCount</c> не блокировал апрув новой задачи.
///     Без этого хэндлера проверка работ падала с
///     <c>ProgressErrors.CounterCannotExceedTotal</c> когда автор публиковал ещё
///     одну задачу в уже стартованный проект.
/// </summary>
public sealed class IssuePublishedHandler
{
    private readonly IProjectProgressRepository _projectProgressRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<IssuePublishedHandler> _logger;

    public IssuePublishedHandler(
        IProjectProgressRepository projectProgressRepository,
        ITransactionManager transactionManager,
        ILogger<IssuePublishedHandler> logger)
    {
        _projectProgressRepository = projectProgressRepository;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task Handle(IssuePublished message, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProjectProgress> rows = await _projectProgressRepository.GetManyByAsync(
            p => p.ProjectId == message.ProjectId, cancellationToken);

        if (rows.Count == 0)
        {
            return;
        }

        foreach (ProjectProgress progress in rows)
        {
            progress.RegisterIssueAdded();
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to update project progress after IssuePublished {IssueId}: {Type}",
                message.IssueId,
                saveResult.Error.Type);
            throw saveResult.Error.ToException();
        }

        _logger.LogInformation(
            "IssuePublished {IssueId} (project {ProjectId}): bumped TotalIssuesCount on {Count} progress rows",
            message.IssueId,
            message.ProjectId,
            rows.Count);
    }
}
