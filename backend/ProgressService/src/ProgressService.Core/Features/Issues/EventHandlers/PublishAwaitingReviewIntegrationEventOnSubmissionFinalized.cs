using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Database;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using ProgressService.Domain.IssueSubmissions;
using Shared.Messaging.IntegrationEvents.Progress.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Issues.EventHandlers;

/// <summary>
/// Notification GAP #334 — когда submission возвращается к автору на ручное ревью
/// (AI не справилась → авто-Finalize в gate, или студент нажал «Отправить автору»),
/// шлём автору свежее <see cref="IssueSubmissionAwaitingReview"/>. При AI-гейте
/// submission пропадала из inbox'а, а исходное create-time уведомление устаревало —
/// без этого автор не узнавал, что задача вернулась к нему.
///
/// Переиспользует тот же integration event + author-notification pipeline, что и
/// create-time publisher. ARS идемпотентно no-op'ит (AiReview под submission уже есть).
/// Enrichment AuthorId — через <see cref="IEducationContentServiceClient"/>, как в
/// <see cref="PublishAwaitingReviewIntegrationEventOnSubmissionCreated"/>.
/// </summary>
public sealed class PublishAwaitingReviewIntegrationEventOnSubmissionFinalized
    : IDomainEventHandler<IssueSubmissionAwaitingManualReviewEvent>
{
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly ICourseEnrollmentRepository _enrollmentRepository;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<PublishAwaitingReviewIntegrationEventOnSubmissionFinalized> _logger;

    public PublishAwaitingReviewIntegrationEventOnSubmissionFinalized(
        IIssueProgressRepository issueProgressRepository,
        ICourseEnrollmentRepository enrollmentRepository,
        IEducationContentServiceClient ecsClient,
        IOutboxService outboxService,
        ILogger<PublishAwaitingReviewIntegrationEventOnSubmissionFinalized> logger)
    {
        _issueProgressRepository = issueProgressRepository;
        _enrollmentRepository = enrollmentRepository;
        _ecsClient = ecsClient;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(IssueSubmissionAwaitingManualReviewEvent domainEvent, CancellationToken ct)
    {
        IssueSubmission submission = domainEvent.Submission;

        Result<IssueProgress, Error> progressResult = await _issueProgressRepository
            .GetByAsync(x => x.Id == submission.IssueProgressId, ct);
        if (progressResult.IsFailure)
            return progressResult.Error;
        IssueProgress progress = progressResult.Value;

        Result<CourseEnrollment, Error> enrollmentResult = await _enrollmentRepository
            .GetByAsync(x => x.Id == progress.EnrollmentId, ct);
        if (enrollmentResult.IsFailure)
            return enrollmentResult.Error;
        CourseEnrollment enrollment = enrollmentResult.Value;

        Result<CourseDto, Error> courseResult = await _ecsClient.GetCourseLookupAsync(enrollment.CourseId, ct);
        if (courseResult.IsFailure)
        {
            // Best-effort: re-notifying the author on a gate-reopen is a side-effect.
            // Unlike the create-time publisher (which fails-closed at submit), finalize
            // must NEVER roll back because ECS is momentarily unreachable — the student
            // already submitted and the author got the create-time notification. Swallow
            // the lookup failure so the gate flag still flips to open.
            _logger.LogWarning(
                "Could not resolve AuthorId for course {CourseId} to publish manual-review IssueSubmissionAwaitingReview; finalize proceeds without re-notification: {Error}",
                enrollment.CourseId,
                courseResult.Error.Messages[0].Message);
            return UnitResult.Success<Error>();
        }

        DateTimeOffset submittedAt = new(DateTime.SpecifyKind(submission.SubmittedAt, DateTimeKind.Utc));

        await _outboxService.PublishAsync(new IssueSubmissionAwaitingReview(
            SubmissionId: submission.Id,
            StudentUserId: enrollment.UserId,
            AuthorId: courseResult.Value.AuthorId,
            IssueId: progress.IssueId,
            CourseId: enrollment.CourseId,
            SubmittedAt: submittedAt,
            Payload: submission.Payload.Value));

        _logger.LogInformation(
            "Published manual-review IssueSubmissionAwaitingReview: SubmissionId={SubmissionId} Author={AuthorId} Issue={IssueId}",
            submission.Id, courseResult.Value.AuthorId, progress.IssueId);

        return UnitResult.Success<Error>();
    }
}
