using Core.Database;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.IssueSubmissions;
using Shared.Messaging.IntegrationEvents.AssignmentReview;

namespace ProgressService.Core.Features.IssueSubmissions.Handlers;

/// <summary>
///     Wolverine consumer для <c>assignment_review.events / ai_review.iteration.completed</c>.
///     Phase 8 (#15) — обновляет 4 denorm AI-поля на <see cref="IssueSubmission"/>:
///     <see cref="IssueSubmission.LatestAiVerdict"/>, <see cref="IssueSubmission.AiIterationsCount"/>,
///     <see cref="IssueSubmission.LastAiIterationAt"/>, <see cref="IssueSubmission.AiReviewStatus"/>.
///
///     <see cref="IssueSubmission.ReadyForHumanReview"/> НЕ меняется здесь —
///     это исключительно поле студента (через <c>Finalize</c> endpoint).
/// </summary>
public sealed class AiReviewIterationCompletedHandler
{
    // Сентинел-«ревьюер» для авто-гейта по AI-вердикту. Решение принимает система,
    // но доменные методы StartReview/Approve требуют reviewerId — даём фиксированный
    // distinct-guid, по которому UI/enrichment узнаёт AI-ревьюера.
    private static readonly Guid AI_REVIEWER_ID = new("a1a1a1a1-0000-0000-0000-00000000a1a1");

    private readonly IIssueSubmissionRepository _submissions;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<AiReviewIterationCompletedHandler> _logger;

    public AiReviewIterationCompletedHandler(
        IIssueSubmissionRepository submissions,
        ITransactionManager transactions,
        ILogger<AiReviewIterationCompletedHandler> logger)
    {
        _submissions = submissions;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task HandleAsync(AiReviewIterationCompleted message, CancellationToken ct)
    {
        var submissionResult = await _submissions.GetByAsync(s => s.Id == message.SubmissionId, ct);
        if (submissionResult.IsFailure)
        {
            // Submission либо удалена (cascade), либо event пришёл раньше БД-видимости.
            // Логируем и no-op — Wolverine retry'ит сам, если transient.
            _logger.LogInformation(
                "AiReviewIterationCompleted received для несуществующей submission {SubmissionId}: {Code}",
                message.SubmissionId,
                submissionResult.Error.Messages[0].Code);
            return;
        }

        IssueSubmission submission = submissionResult.Value;
        // Verdict пустой при failed iteration — мы храним null для UI consistency.
        // AiReview status: если verdict есть → READY, иначе FAILED. Для running state
        // (промежуточный) Phase 8 не получает событий — только в Phase 12+.
        string aiReviewStatus = string.IsNullOrEmpty(message.Verdict) ? "FAILED" : "READY";
        DateTime completedAt = message.CompletedAt.UtcDateTime;

        // Stale-event guard: at-least-once delivery может переиграть старый envelope
        // после того как более новая итерация уже применилась. Пропускаем строго более
        // старые события, чтобы не регрессировать денорм-поля и не пере-гейтить.
        if (message.IterationNumber < submission.AiIterationsCount)
        {
            _logger.LogInformation(
                "Stale AiReviewIterationCompleted (iter {Iter} < current {Current}) for submission {SubmissionId} — skip.",
                message.IterationNumber,
                submission.AiIterationsCount,
                message.SubmissionId);
            return;
        }

        submission.ApplyAiIteration(
            verdict: string.IsNullOrEmpty(message.Verdict) ? null : message.Verdict,
            iterationsCount: message.IterationNumber,
            completedAt: completedAt,
            aiReviewStatus: aiReviewStatus);

        // Авто-гейт по вердикту (workflow #16): AI ведёт цикл, автор — escape hatch.
        // Гейтим только из PENDING — если автор уже взял работу (IN_REVIEW) или решение
        // уже принято (APPROVED/CHANGES_REQUESTED), AI не перетирает его.
        if (submission.ReviewStatus == IssueSubmissionReviewStatus.PENDING)
        {
            ApplyVerdictGate(submission, message.Verdict);
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
        {
            // Бросаем, чтобы Wolverine отработал retry/DLQ. Раньше тут был log+return →
            // envelope ack'ался как успешный, и денорм AI-полей терялся безвозвратно на
            // transient DB-ошибке. Handler идемпотентен (stale-event guard + upsert
            // ApplyAiIteration), поэтому повтор безопасен. Зеркалит CourseCreatedHandler.
            _logger.LogWarning(
                "Failed to denorm AI iteration on submission {SubmissionId}: {Code} — rethrowing for Wolverine retry",
                message.SubmissionId,
                save.Error.Messages[0].Code);
            throw save.Error.ToException();
        }
    }

    /// <summary>
    ///     Переводит submission в финальное состояние по вердикту AI (вердикт-политика #383 —
    ///     AI ассистент, не вахтёр):
    ///     <list type="bullet">
    ///         <item><c>LOOKS_GOOD</c> / <c>MINOR_ISSUES</c> → авто-Approve. Мелкие/необязательные
    ///         замечания НЕ блокируют ученика — фидбэк остаётся виден в истории ревью и
    ///         inline-комментах PR, поправит по желанию;</item>
    ///         <item><c>MAJOR_ISSUES</c> / <c>OFF_TOPIC</c> → RequestChanges (задача реально не
    ///         решена / PR не по теме — студент дорабатывает и ре-сабмитит);</item>
    ///         <item>пустой вердикт (AI сломалась) → Finalize → submission уходит на ручное ревью.</item>
    ///     </list>
    ///     Автор по умолчанию ВНЕ цикла — подключается, только когда ученик сам его позовёт
    ///     («Позвать автора»). Approve/RequestChanges требуют IN_REVIEW, поэтому сначала
    ///     StartReview от сентинел-AI-ревьюера. Детальный фидбэк живёт в inline-комментах PR;
    ///     в submission кладём только вердикт (денорм) — feedback=null.
    /// </summary>
    private void ApplyVerdictGate(IssueSubmission submission, string? verdict)
    {
        if (string.IsNullOrEmpty(verdict))
        {
            // AI не справилась → отдаём автору на ручное ревью.
            submission.Finalize();
            return;
        }

        UnitResult<Error> start = submission.StartReview(AI_REVIEWER_ID);
        if (start.IsFailure)
        {
            _logger.LogWarning(
                "Auto-gate StartReview failed for submission {SubmissionId}: {Code}",
                submission.Id,
                start.Error.Messages[0].Code);
            return;
        }

        // Вердикт-политика #383: авто-Approve для LOOKS_GOOD и MINOR_ISSUES. «Мелкие
        // замечания» (лишний шаблонный файл, стиль, рассинхрон валидации и т.п.) — это
        // фидбэк, а не блокер: ученик видит их в истории/PR и правит по желанию. Блокируем
        // (RequestChanges) только когда задача реально не решена (MAJOR_ISSUES) или PR не по
        // теме (OFF_TOPIC).
        bool autoApprove =
            string.Equals(verdict, "LOOKS_GOOD", StringComparison.Ordinal)
            || string.Equals(verdict, "MINOR_ISSUES", StringComparison.Ordinal);

        UnitResult<Error> gate = autoApprove
            ? submission.Approve()
            : submission.RequestChanges();

        if (gate.IsFailure)
        {
            _logger.LogWarning(
                "Auto-gate ({Verdict}) failed for submission {SubmissionId}: {Code}",
                verdict,
                submission.Id,
                gate.Error.Messages[0].Code);
        }
    }
}
