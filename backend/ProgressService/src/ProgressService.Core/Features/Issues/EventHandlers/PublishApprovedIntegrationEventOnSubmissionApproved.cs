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
/// При одобрении submission публикует integration event <see cref="IssueSubmissionApproved"/>
/// через Wolverine durable outbox. Event потребляет NotificationService и создаёт уведомление
/// автору submission.
/// </summary>
public sealed class PublishApprovedIntegrationEventOnSubmissionApproved
    : IDomainEventHandler<IssueSubmissionApproveEvent>
{
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly ICourseEnrollmentRepository _enrollmentRepository;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<PublishApprovedIntegrationEventOnSubmissionApproved> _logger;

    public PublishApprovedIntegrationEventOnSubmissionApproved(
        IIssueProgressRepository issueProgressRepository,
        ICourseEnrollmentRepository enrollmentRepository,
        IOutboxService outboxService,
        ILogger<PublishApprovedIntegrationEventOnSubmissionApproved> logger)
    {
        _issueProgressRepository = issueProgressRepository;
        _enrollmentRepository = enrollmentRepository;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(IssueSubmissionApproveEvent domainEvent, CancellationToken ct)
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

        DateTimeOffset reviewedAt = submission.ReviewedAt.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(submission.ReviewedAt.Value, DateTimeKind.Utc))
            : DateTimeOffset.UtcNow;

        await _outboxService.PublishAsync(new IssueSubmissionApproved(
            SubmissionId: submission.Id,
            UserId: enrollment.UserId,
            IssueId: progress.IssueId,
            CourseId: enrollment.CourseId,
            ReviewerId: submission.ReviewerId ?? Guid.Empty,
            Comment: submission.Feedback?.Value,
            ReviewedAt: reviewedAt));

        _logger.LogInformation(
            "Published IssueSubmissionApproved: SubmissionId={SubmissionId} UserId={UserId} IssueId={IssueId}",
            submission.Id, enrollment.UserId, progress.IssueId);

        return UnitResult.Success<Error>();
    }
}
