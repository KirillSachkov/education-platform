using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.Reviews;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace AssignmentReviewService.Core.Features.Reviews.Handlers;

/// <summary>
///     Wolverine consumer для <c>education.events / project.review_context.updated</c>.
///     Snapshot guidelines markdown в локальную таблицу <c>project_review_guidelines</c>;
///     PromptBuilder затем кладёт его в prompt как PROJECT-level context.
///
///     После #320 — единственный consumer этого event'а в ARS (parallel
///     <c>IndexProjectRefRepoHandler</c> вместе с RAG-pipeline'ом удалён).
/// </summary>
public sealed class StoreProjectGuidelinesHandler
{
    private readonly IProjectReviewGuidelinesRepository _guidelines;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<StoreProjectGuidelinesHandler> _logger;

    public StoreProjectGuidelinesHandler(
        IProjectReviewGuidelinesRepository guidelines,
        ITransactionManager transactions,
        ILogger<StoreProjectGuidelinesHandler> logger)
    {
        _guidelines = guidelines;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task HandleAsync(ProjectReviewContextUpdated message, CancellationToken ct)
    {
        ProjectReviewGuidelines? existing = await _guidelines.GetByAsync(
            g => g.ProjectId == message.ProjectId, ct);

        if (existing is null)
        {
            ProjectReviewGuidelines created = ProjectReviewGuidelines.Create(
                message.ProjectId,
                message.AuthorId,
                message.GuidelinesMarkdown);
            await _guidelines.AddAsync(created, ct);
        }
        else
        {
            existing.Update(message.AuthorId, message.GuidelinesMarkdown);
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
        {
            _logger.LogWarning(
                "Failed to persist ProjectReviewGuidelines snapshot for project {ProjectId}: {Code}",
                message.ProjectId,
                save.Error.Messages[0].Code);
            throw save.Error.AsTransient().ToException();
        }
    }
}
