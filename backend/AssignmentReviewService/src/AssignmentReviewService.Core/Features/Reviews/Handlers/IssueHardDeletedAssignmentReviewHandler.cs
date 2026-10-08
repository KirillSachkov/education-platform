using AssignmentReviewService.Core.Database;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace AssignmentReviewService.Core.Features.Reviews.Handlers;

/// <summary>
///     Phase 13+ (#15) cascade cleanup: при <c>issue.hard_deleted</c> сносим:
///     <list type="bullet">
///         <item><c>ai_reviews</c> + <c>ai_review_iterations</c> (cascade via FK)</item>
///         <item><c>issue_review_specs</c> (snapshot ReviewSpec)</item>
///     </list>
///     Идемпотентно через <c>ExecuteDeleteAsync</c> (0 rows OK для повторного event'а).
/// </summary>
public sealed class IssueHardDeletedAssignmentReviewHandler
{
    private readonly IAiReviewsRepository _reviews;
    private readonly IIssueReviewSpecsRepository _specs;
    private readonly ILogger<IssueHardDeletedAssignmentReviewHandler> _logger;

    public IssueHardDeletedAssignmentReviewHandler(
        IAiReviewsRepository reviews,
        IIssueReviewSpecsRepository specs,
        ILogger<IssueHardDeletedAssignmentReviewHandler> logger)
    {
        _reviews = reviews;
        _specs = specs;
        _logger = logger;
    }

    public async Task HandleAsync(IssueHardDeleted message, CancellationToken ct)
    {
        int reviewsDeleted = await _reviews.DeleteByIssueIdAsync(message.IssueId, ct);
        int specsDeleted = await _specs.DeleteByIssueIdAsync(message.IssueId, ct);

        _logger.LogInformation(
            "Cleaned up issue {IssueId}: {ReviewsDeleted} reviews, {SpecsDeleted} specs",
            message.IssueId,
            reviewsDeleted,
            specsDeleted);
    }
}
