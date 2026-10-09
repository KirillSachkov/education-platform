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

/// <summary>
///     Ручная приёмка работы автором/админом («Отметить выполненным», #383). AI — ассистент, а не
///     вахтёр: автор должен мочь принять работу студента ОДНОЙ кнопкой из любого статуса проверки
///     (PENDING без ревью, IN_REVIEW, CHANGES_REQUESTED). Реализовано чейнингом существующих
///     доменных переходов (прецедент — <c>AiReviewIterationCompletedHandler.ApplyVerdictGate</c>):
///     normalize <see cref="IssueProgress"/> → UNDER_REVIEW, затем <see cref="IssueSubmission.ForceApprove"/>,
///     который поднимает тот же <c>IssueSubmissionApproveEvent</c>, что и обычный Approve. Каскад
///     (IssueProgress.Approve → COMPLETED + project/module + integration event
///     <c>issue_submission.approved</c>) отрабатывает без изменений.
/// </summary>
public sealed record MarkIssueCompleteCommand(
    Guid CourseId,
    Guid SubmissionId,
    ApproveIssueRequest Request) : ICommand;

public sealed class MarkIssueCompleteCommandValidator : AbstractValidator<MarkIssueCompleteCommand>
{
    public MarkIssueCompleteCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(MarkIssueCompleteCommand.CourseId)));
        RuleFor(x => x.SubmissionId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(MarkIssueCompleteCommand.SubmissionId)));
        RuleFor(x => x.Request)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired(nameof(MarkIssueCompleteCommand.Request)));
    }
}

public sealed class MarkIssueCompleteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/courses/{courseId:guid}/reviews/issues/{submissionId:guid}/mark-complete",
            async Task<EndpointResult> (
                    Guid courseId,
                    Guid submissionId,
                    ApproveIssueRequest request,
                    MarkIssueCompleteHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new MarkIssueCompleteCommand(courseId, submissionId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.MANAGE);
    }
}

public sealed class MarkIssueCompleteHandler : ICommandHandler<MarkIssueCompleteCommand>
{
    private readonly IIssueSubmissionRepository _issueSubmissionRepository;
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly ICourseEnrollmentRepository _courseEnrollmentRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<MarkIssueCompleteCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<MarkIssueCompleteHandler> _logger;

    public MarkIssueCompleteHandler(
        IIssueSubmissionRepository issueSubmissionRepository,
        IIssueProgressRepository issueProgressRepository,
        ICourseEnrollmentRepository courseEnrollmentRepository,
        ITransactionManager transactionManager,
        IValidator<MarkIssueCompleteCommand> validator,
        UserScopedData user,
        ILogger<MarkIssueCompleteHandler> logger)
    {
        _issueSubmissionRepository = issueSubmissionRepository;
        _issueProgressRepository = issueProgressRepository;
        _courseEnrollmentRepository = courseEnrollmentRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(MarkIssueCompleteCommand command, CancellationToken cancellationToken)
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

        // Уже принято — идемпотентный no-op (повторный клик / двойной запрос). Сам submission и
        // issue-progress уже в терминальном состоянии; новый approve-event поднимать нельзя.
        if (submission.ReviewStatus == IssueSubmissionReviewStatus.APPROVED)
        {
            return UnitResult.Success<Error>();
        }

        // Если shared issue-progress уже COMPLETED — задачу приняли по другой попытке: project/
        // module уже начислены. Тогда только закрываем саму попытку (cascadeProgress=false), без
        // повторного approve-каскада, который упал бы на не-UNDER_REVIEW в IssueProgress.Approve().
        bool issueAlreadyComplete = issueProgress.Status == IssueProgressStatus.COMPLETED;

        // Каскад IssueSubmissionApproveEvent → IssueProgress.Approve() требует UNDER_REVIEW.
        // Нормализуем shared issue-progress сюда из любого не-завершённого статуса (no-op если уже
        // UNDER_REVIEW / COMPLETED).
        UnitResult<Error> ensureUnderReview = issueProgress.EnsureUnderReview();
        if (ensureUnderReview.IsFailure)
        {
            return ensureUnderReview.Error;
        }

        UnitResult<Error> forceApproveResult = submission.ForceApprove(
            _user.UserId,
            feedback,
            cascadeProgress: !issueAlreadyComplete);
        if (forceApproveResult.IsFailure)
        {
            return forceApproveResult.Error;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Issue manually marked complete. SubmissionId: {SubmissionId}, ReviewerId: {ReviewerId}, CourseId: {CourseId}, IssueId: {IssueId}",
            command.SubmissionId,
            _user.UserId,
            command.CourseId,
            issueProgress.IssueId);

        return UnitResult.Success<Error>();
    }
}