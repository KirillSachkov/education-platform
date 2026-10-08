using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace ProgressService.Core.Features.Issues.UseCases;

public sealed record SubmitIssueCommand(Guid CourseId, Guid IssueId, SubmitIssueRequest Request) : ICommand;

public sealed class SubmitIssueCommandValidator : AbstractValidator<SubmitIssueCommand>
{
    public SubmitIssueCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(SubmitIssueCommand.CourseId)));
        RuleFor(x => x.IssueId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(SubmitIssueCommand.IssueId)));
        RuleFor(x => x.Request)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired(nameof(SubmitIssueCommand.Request)));
    }
}

public sealed class SubmitIssueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/courses/{courseId:guid}/issues/{issueId:guid}/submit",
            async Task<EndpointResult<SubmitIssueResponse>> (
                Guid courseId,
                Guid issueId,
                SubmitIssueRequest request,
                SubmitIssueHandler handler,
                CancellationToken cancellationToken) =>
                    await handler.Handle(new SubmitIssueCommand(courseId, issueId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class SubmitIssueHandler : ICommandHandler<SubmitIssueResponse, SubmitIssueCommand>
{
    private readonly IEnrollmentAnchorService _enrollmentAnchorService;
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly IIssueSubmissionRepository _issueSubmissionRepository;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly ITransactionManager _transactionManager;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IValidator<SubmitIssueCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<SubmitIssueHandler> _logger;

    public SubmitIssueHandler(
        IEnrollmentAnchorService enrollmentAnchorService,
        IIssueProgressRepository issueProgressRepository,
        IIssueSubmissionRepository issueSubmissionRepository,
        IEducationContentServiceClient ecsClient,
        ITransactionManager transactionManager,
        IEntitlementChecker entitlementChecker,
        IValidator<SubmitIssueCommand> validator,
        UserScopedData user,
        ILogger<SubmitIssueHandler> logger)
    {
        _enrollmentAnchorService = enrollmentAnchorService;
        _issueProgressRepository = issueProgressRepository;
        _issueSubmissionRepository = issueSubmissionRepository;
        _ecsClient = ecsClient;
        _transactionManager = transactionManager;
        _entitlementChecker = entitlementChecker;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<SubmitIssueResponse, Error>> Handle(SubmitIssueCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Per-item entitlement: конкретный issue может быть gated по плану. Resource-теги в Redis
        // выставляются EducationContentService при создании/обновлении issue. Проверяем ДО
        // ensure-create — неавторизованный пользователь не должен материализовать anchor.
        AccessSubject accessSubject = _user.ToAccessSubject();
        AccessDecision accessDecision = await _entitlementChecker.CheckAccessAsync(
            accessSubject,
            ResourceTypes.ISSUE,
            command.IssueId,
            cancellationToken);

        if (!accessDecision.IsGranted)
        {
            return ProgressErrors.IssueLockedForTrial();
        }

        // Capability check: даже если access granted, сама возможность *отправлять* решения
        // требует SUBMIT_ISSUES capability.
        bool canSubmit = await _entitlementChecker.HasCapabilityAsync(
            accessSubject, "SUBMIT_ISSUES", cancellationToken);
        if (!canSubmit)
        {
            return ProgressErrors.IssueSubmissionNotAllowedByPlan();
        }

        // Lazy progress-anchor (access-derive-model Phase 2): ensure-create вместо
        // GetByAsync→NotFound. Доступ уже проверен выше; anchor нужен как FK-родитель прогресса.
        Result<CourseDto, Error> courseLookup = await _ecsClient
            .GetCourseLookupAsync(command.CourseId, cancellationToken);
        if (courseLookup.IsFailure)
        {
            return courseLookup.Error;
        }

        Result<CourseEnrollment, Error> enrollmentResult = await _enrollmentAnchorService
            .EnsureEnrollmentAsync(
                _user.UserId,
                command.CourseId,
                courseLookup.Value.AuthorId,
                EnrollmentSource.ENGAGEMENT,
                cancellationToken);
        if (enrollmentResult.IsFailure)
        {
            return enrollmentResult.Error;
        }

        Result<IssueProgress, Error> issueProgressResult = await _issueProgressRepository
            .GetByAsync(
                x => x.EnrollmentId == enrollmentResult.Value.Id && x.IssueId == command.IssueId,
                cancellationToken);
        if (issueProgressResult.IsFailure)
        {
            return issueProgressResult.Error;
        }

        IssueProgress issueProgress = issueProgressResult.Value;

        Result<IssueDto, Error> issueLookup = await _ecsClient
            .GetIssueLookupAsync(issueProgress.ProjectId, command.IssueId, cancellationToken);
        if (issueLookup.IsFailure)
        {
            return issueLookup.Error;
        }

        bool isSelfCheck = string.Equals(
            issueLookup.Value.SubmissionMode,
            "SELF_CHECK",
            StringComparison.OrdinalIgnoreCase);

        Result<IssueSubmissionPayload, Error> payloadResult = isSelfCheck
            ? IssueSubmissionPayload.CreateSelfCheck(command.Request.ContentPayload)
            : IssueSubmissionPayload.Create(command.Request.SubmissionUrl);
        if (payloadResult.IsFailure)
        {
            return payloadResult.Error;
        }

        UnitResult<Error> submitForReviewResult = issueProgress.SubmitForReview();
        if (submitForReviewResult.IsFailure)
        {
            return submitForReviewResult.Error;
        }

        Result<int, Error> maxAttemptNumberResult = await _issueSubmissionRepository
            .GetMaxAttemptNumberAsync(issueProgress.Id, cancellationToken);
        if (maxAttemptNumberResult.IsFailure)
        {
            return maxAttemptNumberResult.Error;
        }

        Result<AttemptNumber, Error> attemptNumberResult = AttemptNumber.Create(maxAttemptNumberResult.Value + 1);
        if (attemptNumberResult.IsFailure)
        {
            return attemptNumberResult.Error;
        }

        Result<IssueSubmission, Error> submissionResult = IssueSubmission.Create(
            issueProgress.Id,
            attemptNumberResult.Value,
            payloadResult.Value,
            autoFinalize: true,
            raiseAwaitingReview: !isSelfCheck);
        if (submissionResult.IsFailure)
        {
            return submissionResult.Error;
        }

        if (isSelfCheck)
        {
            UnitResult<Error> approveResult = submissionResult.Value.ForceApprove(_user.UserId);
            if (approveResult.IsFailure)
            {
                return approveResult.Error;
            }
        }

        await _issueSubmissionRepository.AddAsync(submissionResult.Value, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Issue submitted successfully. SubmissionId: {SubmissionId}, UserId: {UserId}, CourseId: {CourseId}, IssueId: {IssueId}, SubmissionMode: {SubmissionMode}",
            submissionResult.Value.Id,
            _user.UserId,
            command.CourseId,
            command.IssueId,
            issueLookup.Value.SubmissionMode);

        return new SubmitIssueResponse(submissionResult.Value.Id);
    }
}
