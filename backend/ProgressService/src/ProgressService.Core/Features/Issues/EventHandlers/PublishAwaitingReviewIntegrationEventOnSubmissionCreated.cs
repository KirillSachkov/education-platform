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
/// После успешного создания submission публикует integration event
/// <see cref="IssueSubmissionAwaitingReview"/> через Wolverine durable outbox.
///
/// Для события нужно резолвить AuthorId курса — берётся через
/// <see cref="IEducationContentServiceClient"/>. Если ECS недоступен, handler возвращает ошибку —
/// Wolverine попробует retry; при стабильном отказе ECS события будут dead-letter'ом, это лучше
/// чем отправить уведомление «в никуда» без автора.
/// </summary>
public sealed class PublishAwaitingReviewIntegrationEventOnSubmissionCreated
    : IDomainEventHandler<IssueSubmissionCreatedEvent>
{
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly ICourseEnrollmentRepository _enrollmentRepository;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<PublishAwaitingReviewIntegrationEventOnSubmissionCreated> _logger;

    public PublishAwaitingReviewIntegrationEventOnSubmissionCreated(
        IIssueProgressRepository issueProgressRepository,
        ICourseEnrollmentRepository enrollmentRepository,
        IEducationContentServiceClient ecsClient,
        IOutboxService outboxService,
        ILogger<PublishAwaitingReviewIntegrationEventOnSubmissionCreated> logger)
    {
        _issueProgressRepository = issueProgressRepository;
        _enrollmentRepository = enrollmentRepository;
        _ecsClient = ecsClient;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(IssueSubmissionCreatedEvent domainEvent, CancellationToken ct)
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
            _logger.LogWarning(
                "Could not resolve AuthorId for course {CourseId} to publish IssueSubmissionAwaitingReview: {Error}",
                enrollment.CourseId,
                courseResult.Error.Messages[0].Message);
            return courseResult.Error;
        }

        Result<IssueDto, Error> issueResult = await _ecsClient.GetIssueLookupAsync(progress.ProjectId, progress.IssueId, ct);
        if (issueResult.IsFailure)
        {
            _logger.LogWarning(
                "Could not resolve issue review settings for issue {IssueId} to publish IssueSubmissionAwaitingReview: {Error}",
                progress.IssueId,
                issueResult.Error.Messages[0].Message);
            return issueResult.Error;
        }

        DateTimeOffset submittedAt = new(DateTime.SpecifyKind(submission.SubmittedAt, DateTimeKind.Utc));

        await _outboxService.PublishAsync(new IssueSubmissionAwaitingReview(
            SubmissionId: submission.Id,
            StudentUserId: enrollment.UserId,
            AuthorId: courseResult.Value.AuthorId,
            IssueId: progress.IssueId,
            CourseId: enrollment.CourseId,
            SubmittedAt: submittedAt,
            Payload: submission.Payload.Value,
            AiReviewRequested: issueResult.Value.IsAutoReviewEnabled));

        _logger.LogInformation(
            "Published IssueSubmissionAwaitingReview: SubmissionId={SubmissionId} Student={StudentId} Author={AuthorId} Issue={IssueId} AiReviewRequested={AiReviewRequested}",
            submission.Id,
            enrollment.UserId,
            courseResult.Value.AuthorId,
            progress.IssueId,
            issueResult.Value.IsAutoReviewEnabled);

        return UnitResult.Success<Error>();
    }
}
