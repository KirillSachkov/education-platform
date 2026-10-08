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

public sealed record RequestIssueChangesCommand(
    Guid CourseId,
    Guid SubmissionId,
    RequestIssueChangesRequest Request) : ICommand;

public sealed class RequestIssueChangesCommandValidator : AbstractValidator<RequestIssueChangesCommand>
{
    public RequestIssueChangesCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(RequestIssueChangesCommand.CourseId)));
        RuleFor(x => x.SubmissionId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(RequestIssueChangesCommand.SubmissionId)));
        RuleFor(x => x.Request)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired(nameof(RequestIssueChangesCommand.Request)));
    }
}

public sealed class RequestIssueChangesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/progress/courses/{courseId:guid}/reviews/issues/{submissionId:guid}/request-changes",
            async Task<EndpointResult> (
                Guid courseId,
                Guid submissionId,
                RequestIssueChangesRequest request,
                RequestIssueChangesHandler handler,
                CancellationToken cancellationToken) =>
                    await handler.Handle(new RequestIssueChangesCommand(courseId, submissionId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.MANAGE);
}

public sealed class RequestIssueChangesHandler : ICommandHandler<RequestIssueChangesCommand>
{
    private readonly IIssueSubmissionRepository _issueSubmissionRepository;
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly ICourseEnrollmentRepository _courseEnrollmentRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<RequestIssueChangesCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<RequestIssueChangesHandler> _logger;

    public RequestIssueChangesHandler(
        IIssueSubmissionRepository issueSubmissionRepository,
        IIssueProgressRepository issueProgressRepository,
        ICourseEnrollmentRepository courseEnrollmentRepository,
        ITransactionManager transactionManager,
        IValidator<RequestIssueChangesCommand> validator,
        UserScopedData user,
        ILogger<RequestIssueChangesHandler> logger)
    {
        _issueSubmissionRepository = issueSubmissionRepository;
        _issueProgressRepository = issueProgressRepository;
        _courseEnrollmentRepository = courseEnrollmentRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(RequestIssueChangesCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        IssueReviewFeedback? feedback = null;
        if (!string.IsNullOrWhiteSpace(command.Request.Feedback))
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

        UnitResult<Error> requestChangesResult = submission.RequestChanges(feedback);
        if (requestChangesResult.IsFailure)
        {
            return requestChangesResult.Error;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Issue changes requested successfully. SubmissionId: {SubmissionId}, ReviewerId: {ReviewerId}, CourseId: {CourseId}",
            command.SubmissionId,
            _user.UserId,
            command.CourseId);

        return UnitResult.Success<Error>();
    }
}
