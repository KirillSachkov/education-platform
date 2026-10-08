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
/// При запросе изменений по submission публикует integration event
/// <see cref="IssueSubmissionChangesRequested"/> через Wolverine durable outbox.
/// NotificationService получит это событие и создаст уведомление автору submission.
/// </summary>
public sealed class PublishChangesRequestedIntegrationEventOnSubmissionChangesRequested
    : IDomainEventHandler<IssueSubmissionChangesRequestedEvent>
{
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly ICourseEnrollmentRepository _enrollmentRepository;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<PublishChangesRequestedIntegrationEventOnSubmissionChangesRequested> _logger;

    public PublishChangesRequestedIntegrationEventOnSubmissionChangesRequested(
        IIssueProgressRepository issueProgressRepository,
        ICourseEnrollmentRepository enrollmentRepository,
        IOutboxService outboxService,
        ILogger<PublishChangesRequestedIntegrationEventOnSubmissionChangesRequested> logger)
    {
        _issueProgressRepository = issueProgressRepository;
        _enrollmentRepository = enrollmentRepository;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(
        IssueSubmissionChangesRequestedEvent domainEvent,
        CancellationToken ct)
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

        // Feedback обязателен для changes_requested (см. IssueSubmission.RequestChanges()),
        // но на всякий случай не падаем, если Value пустое — шлём пустую строку.
        string comment = submission.Feedback?.Value ?? string.Empty;

        await _outboxService.PublishAsync(new IssueSubmissionChangesRequested(
            SubmissionId: submission.Id,
            UserId: enrollment.UserId,
            IssueId: progress.IssueId,
            CourseId: enrollment.CourseId,
            ReviewerId: submission.ReviewerId ?? Guid.Empty,
            Comment: comment,
            ReviewedAt: reviewedAt));

        _logger.LogInformation(
            "Published IssueSubmissionChangesRequested: SubmissionId={SubmissionId} UserId={UserId} IssueId={IssueId}",
            submission.Id, enrollment.UserId, progress.IssueId);

        return UnitResult.Success<Error>();
    }
}
