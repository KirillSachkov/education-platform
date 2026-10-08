using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.Reviews;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace AssignmentReviewService.Core.Features.Reviews.Handlers;

/// <summary>
///     Wolverine consumer для <c>education.events / issue.review_spec.updated</c>.
///     Денормализует ReviewSpec из ECS в локальную таблицу <c>issue_review_specs</c>.
///     Используется PromptBuilder'ом для построения instructions block'а
///     в run-iteration pipeline (Phase 7).
///
///     Идемпотентность: по <see cref="ReviewSpecUpdated.IssueId"/> делаем upsert
///     (find → update | create).
/// </summary>
public sealed class StoreIssueReviewSpecHandler
{
    private readonly IIssueReviewSpecsRepository _specs;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<StoreIssueReviewSpecHandler> _logger;

    public StoreIssueReviewSpecHandler(
        IIssueReviewSpecsRepository specs,
        ITransactionManager transactions,
        ILogger<StoreIssueReviewSpecHandler> logger)
    {
        _specs = specs;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task HandleAsync(ReviewSpecUpdated message, CancellationToken ct)
    {
        IssueReviewSpec? existing = await _specs.GetByAsync(s => s.IssueId == message.IssueId, ct);

        if (existing is null)
        {
            IssueReviewSpec created = IssueReviewSpec.Create(
                message.IssueId,
                message.ProjectId,
                message.AuthorId,
                message.AuthorPrompt,
                message.ReviewAspects);
            await _specs.AddAsync(created, ct);
        }
        else
        {
            existing.Update(
                message.ProjectId,
                message.AuthorId,
                message.AuthorPrompt,
                message.ReviewAspects);
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
        {
            _logger.LogWarning(
                "Failed to persist IssueReviewSpec snapshot for issue {IssueId}: {Code}",
                message.IssueId,
                save.Error.Messages[0].Code);
            throw save.Error.AsTransient().ToException();
        }
    }
}
