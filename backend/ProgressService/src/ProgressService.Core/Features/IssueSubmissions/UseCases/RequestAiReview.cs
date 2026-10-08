using Core.Abstractions;
using Core.Database;
using Core.Validation;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Database;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using Shared.Messaging.IntegrationEvents.Progress.Events;
using SharedKernel;

namespace ProgressService.Core.Features.IssueSubmissions.UseCases;

public sealed record RequestAiReviewCommand(Guid SubmissionId) : ICommand;

public sealed class RequestAiReviewValidator : AbstractValidator<RequestAiReviewCommand>
{
    public RequestAiReviewValidator()
    {
        RuleFor(x => x.SubmissionId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(RequestAiReviewCommand.SubmissionId)));
    }
}

public sealed class RequestAiReviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/submissions/{submissionId:guid}/request-ai-review/",
                async Task<EndpointResult> (
                    [FromRoute] Guid submissionId,
                    [FromServices] RequestAiReviewHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new RequestAiReviewCommand(submissionId), ct))
            .RequirePermissions(PlatformPermissions.Progress.MANAGE);
    }
}

/// <summary>
///     #383 «Запустить AI принудительно»: автор/админ форсит AI-проверку для submission,
///     у которой ещё нет <c>AiReview</c> (auto-review был выключен / не было VCS installation
///     в момент сабмита). Переиспользует существующую инфраструктуру — НЕ добавляет ARS-эндпоинт:
///     ре-публикует <see cref="IssueSubmissionAwaitingReview"/> для этой submission. Идемпотентный
///     ARS-handler создаёт+запускает AiReview если его нет, no-op'ит если уже есть.
///
///     Payload собирается ровно как в <c>PublishAwaitingReviewIntegrationEventOnSubmissionCreated</c>:
///     PR URL — из <c>submission.Payload</c>, AuthorId — резолвится через ECS, IssueId/CourseId —
///     из issue_progress / enrollment.
/// </summary>
public sealed class RequestAiReviewHandler : ICommandHandler<RequestAiReviewCommand>
{
    private readonly IIssueSubmissionRepository _submissions;
    private readonly IIssueProgressRepository _progresses;
    private readonly ICourseEnrollmentRepository _enrollments;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly IValidator<RequestAiReviewCommand> _validator;
    private readonly ILogger<RequestAiReviewHandler> _logger;

    public RequestAiReviewHandler(
        IIssueSubmissionRepository submissions,
        IIssueProgressRepository progresses,
        ICourseEnrollmentRepository enrollments,
        IEducationContentServiceClient ecsClient,
        IOutboxService outbox,
        ITransactionManager transactions,
        IValidator<RequestAiReviewCommand> validator,
        ILogger<RequestAiReviewHandler> logger)
    {
        _submissions = submissions;
        _progresses = progresses;
        _enrollments = enrollments;
        _ecsClient = ecsClient;
        _outbox = outbox;
        _transactions = transactions;
        _validator = validator;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(RequestAiReviewCommand command, CancellationToken ct)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return validation.ToError();

        Result<IssueSubmission, Error> submissionResult = await _submissions.GetByAsync(
            s => s.Id == command.SubmissionId, ct);
        if (submissionResult.IsFailure)
            return submissionResult.Error;

        IssueSubmission submission = submissionResult.Value;

        Result<IssueProgress, Error> progressResult = await _progresses.GetByAsync(
            p => p.Id == submission.IssueProgressId, ct);
        if (progressResult.IsFailure)
            return progressResult.Error;

        IssueProgress progress = progressResult.Value;

        Result<CourseEnrollment, Error> enrollmentResult = await _enrollments.GetByAsync(
            e => e.Id == progress.EnrollmentId, ct);
        if (enrollmentResult.IsFailure)
            return enrollmentResult.Error;

        CourseEnrollment enrollment = enrollmentResult.Value;

        Result<CourseDto, Error> courseResult = await _ecsClient.GetCourseLookupAsync(enrollment.CourseId, ct);
        if (courseResult.IsFailure)
            return courseResult.Error;

        DateTimeOffset submittedAt = new(DateTime.SpecifyKind(submission.SubmittedAt, DateTimeKind.Utc));

        // Ре-публикуем awaiting_review — ARS `IssueSubmissionAwaitingReviewHandler` идемпотентен
        // (ExistsAsync early-out + unique-violation catch), так что at-least-once доставка/retry
        // не создаст дубль AiReview. НЕ добавлять локальный write-ahead guard «уже публиковали»:
        // он сломает легитимный кейс force-start, когда AiReview ещё нет (нужна именно ре-публикация).
        await _outbox.PublishAsync(new IssueSubmissionAwaitingReview(
            SubmissionId: submission.Id,
            StudentUserId: enrollment.UserId,
            AuthorId: courseResult.Value.AuthorId,
            IssueId: progress.IssueId,
            CourseId: enrollment.CourseId,
            SubmittedAt: submittedAt,
            Payload: submission.Payload.Value,
            AiReviewRequested: true));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation(
            "AI review force-requested for submission {SubmissionId}: re-published IssueSubmissionAwaitingReview (Author={AuthorId}, Issue={IssueId}).",
            submission.Id,
            courseResult.Value.AuthorId,
            progress.IssueId);
        return UnitResult.Success<Error>();
    }
}
