using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ProgressService.Contracts.Requests;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace ProgressService.Core.Features.Reviews.UseCases;

public sealed record ApproveIssueCommand(
    Guid CourseId,
    Guid SubmissionId,
    ApproveIssueRequest Request) : ICommand;

public sealed class ApproveIssueCommandValidator : AbstractValidator<ApproveIssueCommand>
{
    public ApproveIssueCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(ApproveIssueCommand.CourseId)));
        RuleFor(x => x.SubmissionId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(ApproveIssueCommand.SubmissionId)));
        RuleFor(x => x.Request)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired(nameof(ApproveIssueCommand.Request)));
    }
}

public sealed class ApproveIssueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/courses/{courseId:guid}/reviews/issues/{submissionId:guid}/approve",
            async Task<EndpointResult> (
                    Guid courseId,
                    Guid submissionId,
                    ApproveIssueRequest request,
                    ApproveIssueHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new ApproveIssueCommand(courseId, submissionId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.MANAGE);
    }
}

public sealed class ApproveIssueHandler : ICommandHandler<ApproveIssueCommand>
{
    private readonly IIssueSubmissionRepository _issueSubmissionRepository;
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly ICourseEnrollmentRepository _courseEnrollmentRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<ApproveIssueCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<ApproveIssueHandler> _logger;

    public ApproveIssueHandler(
        IIssueSubmissionRepository issueSubmissionRepository,
        IIssueProgressRepository issueProgressRepository,
        ICourseEnrollmentRepository courseEnrollmentRepository,
        ITransactionManager transactionManager,
        IValidator<ApproveIssueCommand> validator,
        UserScopedData user,
        ILogger<ApproveIssueHandler> logger)
    {
        _issueSubmissionRepository = issueSubmissionRepository;
        _issueProgressRepository = issueProgressRepository;
        _courseEnrollmentRepository = courseEnrollmentRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(ApproveIssueCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        IssueReviewFeedback? feedback = null;
        if (command.Request.Feedback is not null)
        {
            Result<IssueReviewFeedback, Error> feedbackResult = IssueReviewFeedback.Create(command.Request.Feedback);
            if (feedbackResult.IsFailure)
            {
                return feedbackResult.Error;
            }

            feedback = feedbackResult.Value;
        }

        Result<IssueSubmission, Error> submissionResult = await _issueSubmissionRepository
            .GetByAsync(x => x.Id == command.SubmissionId, cancellationToken);
        if (submissionResult.IsFailure)
        {
            return submissionResult.Error;
        }

        IssueSubmission submission = submissionResult.Value;

        Result<IssueProgress, Error> issueProgressResult = await _issueProgressRepository
            .GetByAsync(x => x.Id == submission.IssueProgressId, cancellationToken);
        if (issueProgressResult.IsFailure)
        {
            return issueProgressResult.Error;
        }

        IssueProgress issueProgress = issueProgressResult.Value;

        Result<CourseEnrollment, Error> enrollmentResult = await _courseEnrollmentRepository
            .GetByAsync(x => x.Id == issueProgress.EnrollmentId && x.CourseId == command.CourseId, cancellationToken);
        if (enrollmentResult.IsFailure)
        {
            return enrollmentResult.Error;
        }

        CourseEnrollment enrollment = enrollmentResult.Value;
        bool isPrivileged = _user.HasRole(PlatformRoles.ADMIN) || _user.HasRole(PlatformRoles.MODERATOR);
        bool isAuthorOwner = _user.HasRole(PlatformRoles.AUTHOR) && enrollment.AuthorId == _user.UserId;
        if (!isPrivileged && !isAuthorOwner)
        {
            return Error.Authorization("review.not.authorized", "Нет прав на рецензирование в этом курсе");
        }

        UnitResult<Error> approveSubmissionResult = submission.Approve(feedback);
        if (approveSubmissionResult.IsFailure)
        {
            return approveSubmissionResult.Error;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Issue approved successfully. SubmissionId: {SubmissionId}, ReviewerId: {ReviewerId}, CourseId: {CourseId}, IssueId: {IssueId}",
            command.SubmissionId,
            _user.UserId,
            command.CourseId,
            issueProgress.IssueId);

        return UnitResult.Success<Error>();
    }
}