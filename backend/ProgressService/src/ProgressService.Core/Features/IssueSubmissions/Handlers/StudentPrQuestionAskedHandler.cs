using Core.Database;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.IssueSubmissions;
using Shared.Messaging.IntegrationEvents.AssignmentReview;

namespace ProgressService.Core.Features.IssueSubmissions.Handlers;

/// <summary>
///     Wolverine consumer для <c>assignment_review.events / student_pr_question.asked</c> (#713).
///     Денормит время последнего вопроса студента в его GitHub-PR на
///     <see cref="IssueSubmission.StudentQuestionAt"/> — питает бейдж «новый вопрос от студента»
///     на карточке сдачи в панели «Проверка работ» (list-эндпоинты pending/in-review/reviewed).
///
///     Зеркалит стиль <c>AiReviewIterationCompletedHandler</c>: submission не найдена →
///     log + no-op; save-fail → rethrow для Wolverine retry (handler идемпотентен —
///     <see cref="IssueSubmission.RecordStudentQuestion"/> двигает timestamp только вперёд,
///     повтор безопасен).
/// </summary>
public sealed class StudentPrQuestionAskedHandler
{
    private readonly IIssueSubmissionRepository _submissions;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<StudentPrQuestionAskedHandler> _logger;

    public StudentPrQuestionAskedHandler(
        IIssueSubmissionRepository submissions,
        ITransactionManager transactions,
        ILogger<StudentPrQuestionAskedHandler> logger)
    {
        _submissions = submissions;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task HandleAsync(StudentPrQuestionAsked message, CancellationToken ct)
    {
        var submissionResult = await _submissions.GetByAsync(s => s.Id == message.SubmissionId, ct);
        if (submissionResult.IsFailure)
        {
            // Submission удалена (cascade) или event опередил БД-видимость. Log + no-op —
            // Wolverine retry'ит сам, если transient.
            _logger.LogInformation(
                "StudentPrQuestionAsked received для несуществующей submission {SubmissionId}: {Code}",
                message.SubmissionId,
                submissionResult.Error.Messages[0].Code);
            return;
        }

        IssueSubmission submission = submissionResult.Value;
        submission.RecordStudentQuestion(message.CreatedAt.UtcDateTime);

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
        {
            _logger.LogWarning(
                "Failed to denorm student question on submission {SubmissionId}: {Code} — rethrowing for Wolverine retry",
                message.SubmissionId,
                save.Error.Messages[0].Code);
            throw save.Error.ToException();
        }
    }
}
