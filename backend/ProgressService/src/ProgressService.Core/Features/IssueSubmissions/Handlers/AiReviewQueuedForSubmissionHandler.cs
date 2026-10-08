using Core.Database;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.IssueSubmissions;
using Shared.Messaging.IntegrationEvents.AssignmentReview;

namespace ProgressService.Core.Features.IssueSubmissions.Handlers;

/// <summary>
///     Wolverine consumer для <c>assignment_review.events / ai_review.queued_for_submission</c>.
///     Phase 8 (#15) — гейтит submission через <c>ReadyForHumanReview=false</c>,
///     чтобы автор не видел её в inbox'е до завершения AI iteration'а и явного
///     <c>Finalize</c> студентом.
///
///     ARS публикует этот event ТОЛЬКО когда есть active VCS installation
///     (= AI реально может крутить iteration'ы). Если installation отсутствует,
///     event не публикуется — submission остаётся ReadyForHumanReview=true.
/// </summary>
public sealed class AiReviewQueuedForSubmissionHandler
{
    private readonly IIssueSubmissionRepository _submissions;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<AiReviewQueuedForSubmissionHandler> _logger;

    public AiReviewQueuedForSubmissionHandler(
        IIssueSubmissionRepository submissions,
        ITransactionManager transactions,
        ILogger<AiReviewQueuedForSubmissionHandler> logger)
    {
        _submissions = submissions;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task HandleAsync(AiReviewQueuedForSubmission message, CancellationToken ct)
    {
        var submissionResult = await _submissions.GetByAsync(s => s.Id == message.SubmissionId, ct);
        if (submissionResult.IsFailure)
        {
            _logger.LogInformation(
                "AiReviewQueuedForSubmission для несуществующей submission {SubmissionId}: {Code}",
                message.SubmissionId,
                submissionResult.Error.Messages[0].Code);
            return;
        }

        IssueSubmission submission = submissionResult.Value;

        // Гейтим (→ «В проверке») только PENDING-сабмишены. Event прилетает и на
        // первом auto-flow (свежая PENDING-попытка), и на ручном «Перепроверить» (#2).
        // Если автор уже взял работу (IN_REVIEW) или решение принято
        // (APPROVED/CHANGES_REQUESTED) — НЕ перегейчиваем: иначе ready_for_human_review=false
        // на не-PENDING выкинул бы карточку из обоих табов (pending требует ready=true,
        // in-review требует review_status=PENDING).
        if (submission.ReviewStatus != IssueSubmissionReviewStatus.PENDING)
        {
            _logger.LogDebug(
                "Submission {SubmissionId} is {Status}, not PENDING — skip AI re-gate.",
                message.SubmissionId,
                submission.ReviewStatus);
            return;
        }

        DateTime queuedAt = message.QueuedAt.UtcDateTime;
        if (submission.LastAiIterationAt is not null
            && queuedAt <= submission.LastAiIterationAt.Value)
        {
            _logger.LogInformation(
                "Stale AiReviewQueuedForSubmission at {QueuedAt} after iteration completed at {CompletedAt} " +
                "for submission {SubmissionId} — skip.",
                queuedAt,
                submission.LastAiIterationAt.Value,
                message.SubmissionId);
            return;
        }

        submission.GateForAiReview();
        // AI status = QUEUED — НЕ дёргаем `ApplyAiIteration` чтобы не выставлять
        // `LastAiIterationAt = QueuedAt` (никакая iteration ещё не завершилась) и не
        // сбрасывать `AiIterationsCount` если IterationCompleted event опередил Queued
        // event в порядке доставки.
        submission.MarkAiQueued();

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
        {
            _logger.LogWarning(
                "Failed to gate submission {SubmissionId}: {Code} — rethrowing for Wolverine retry",
                message.SubmissionId,
                save.Error.Messages[0].Code);
            throw save.Error.ToException();
        }
    }
}
