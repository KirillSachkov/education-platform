using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ProgressService.Contracts.Requests;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Projects;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace ProgressService.Core.Features.Reviews.UseCases;

/// <summary>
///     Ручной staff-override статуса задачи студенту (#518). Автор/админ/модератор выставляет ЛЮБОЙ
///     статус прогресса (NOT_STARTED / IN_PROGRESS / UNDER_REVIEW / REQUESTED_CHANGES / COMPLETED),
///     минуя обычный workflow (submit → review → approve). Зеркалит auth/reviewer-резолв
///     <see cref="MarkIssueCompleteForUserHandler"/>.
///     <para>
///     Диспетчеризация по целевому статусу:
///     <list type="bullet">
///       <item><b>COMPLETED</b> → переиспользует synthetic-approved путь
///       (<see cref="IStaffIssueCompletionService"/>): синтетический принятый submission + каскад
///       XP/project/module + integration event <c>issue_submission.approved</c>. Reviewer ОБЯЗАТЕЛЕН
///       (fail-closed если пуст).</item>
///       <item><b>NOT_STARTED / IN_PROGRESS / UNDER_REVIEW / REQUESTED_CHANGES</b> → строит/обеспечивает
///       progress-цепочку (enrollment anchor → ProjectProgress → ModuleProgress/ModuleItemProgress если
///       в модуле → IssueProgress), затем применяет staff-переход на IssueProgress
///       (<see cref="IssueProgress.Reset"/> для NOT_STARTED; <see cref="IssueProgress.SetStatusByStaff"/>
///       для остальных). При уходе из COMPLETED поднимается <c>IssueProgressReopenedEvent</c> — откат
///       XP/project/module. Reviewer НЕ требуется.</item>
///     </list>
///     </para>
///     <para>Идемпотентно: target == current → no-op без дублирования событий/XP.</para>
/// </summary>
public sealed record SetIssueStatusForUserCommand(
    Guid CourseId,
    Guid IssueId,
    SetIssueStatusForUserRequest Request) : ICommand;

public sealed class SetIssueStatusForUserCommandValidator : AbstractValidator<SetIssueStatusForUserCommand>
{
    public SetIssueStatusForUserCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(SetIssueStatusForUserCommand.CourseId)));
        RuleFor(x => x.IssueId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(SetIssueStatusForUserCommand.IssueId)));
        RuleFor(x => x.Request)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired(nameof(SetIssueStatusForUserCommand.Request)));
        RuleFor(x => x.Request.UserId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(SetIssueStatusForUserRequest.UserId)));
        RuleFor(x => x.Request.TargetStatus)
            .Must(s => Enum.TryParse<IssueProgressStatus>(s, ignoreCase: false, out _))
            .WithError(GeneralErrors.ValueIsInvalid(nameof(SetIssueStatusForUserRequest.TargetStatus)));
    }
}

public sealed class SetIssueStatusForUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/progress/courses/{courseId:guid}/issues/{issueId:guid}/progress-status-for-user",
            async Task<EndpointResult> (
                    Guid courseId,
                    Guid issueId,
                    SetIssueStatusForUserRequest request,
                    SetIssueStatusForUserHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new SetIssueStatusForUserCommand(courseId, issueId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.MANAGE);
    }
}

public sealed class SetIssueStatusForUserHandler : ICommandHandler<SetIssueStatusForUserCommand>
{
    private readonly IStaffIssueCompletionService _staffIssueCompletion;
    private readonly IEnrollmentAnchorService _enrollmentAnchorService;
    private readonly IProjectProgressRepository _projectProgressRepository;
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly IModuleItemProgressRepository _moduleItemProgressRepository;
    private readonly IModuleProgressService _moduleProgressService;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<SetIssueStatusForUserCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<SetIssueStatusForUserHandler> _logger;

    public SetIssueStatusForUserHandler(
        IStaffIssueCompletionService staffIssueCompletion,
        IEnrollmentAnchorService enrollmentAnchorService,
        IProjectProgressRepository projectProgressRepository,
        IIssueProgressRepository issueProgressRepository,
        IModuleItemProgressRepository moduleItemProgressRepository,
        IModuleProgressService moduleProgressService,
        IEducationContentServiceClient ecsClient,
        ITransactionManager transactionManager,
        IValidator<SetIssueStatusForUserCommand> validator,
        UserScopedData user,
        ILogger<SetIssueStatusForUserHandler> logger)
    {
        _staffIssueCompletion = staffIssueCompletion;
        _enrollmentAnchorService = enrollmentAnchorService;
        _projectProgressRepository = projectProgressRepository;
        _issueProgressRepository = issueProgressRepository;
        _moduleItemProgressRepository = moduleItemProgressRepository;
        _moduleProgressService = moduleProgressService;
        _ecsClient = ecsClient;
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(SetIssueStatusForUserCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        IssueProgressStatus targetStatus = Enum.Parse<IssueProgressStatus>(command.Request.TargetStatus, ignoreCase: false);

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

        // Course-lookup нужен для auth (AuthorId из ECS — enrollment студента может не существовать)
        // и переиспользуется shared-сервисом / enrollment-anchor'ом (AuthorId).
        Result<CourseDto, Error> courseLookupResult = await _ecsClient
            .GetCourseLookupAsync(command.CourseId, cancellationToken);
        if (courseLookupResult.IsFailure)
        {
            return courseLookupResult.Error;
        }

        // Auth: ADMIN | MODERATOR — привилегированные; AUTHOR — только владелец курса. Зеркалит
        // проверку в ApproveIssue / MarkIssueComplete(ForUser).
        bool isPrivileged = _user.HasRole(PlatformRoles.ADMIN) || _user.HasRole(PlatformRoles.MODERATOR);
        bool isAuthorOwner = _user.HasRole(PlatformRoles.AUTHOR)
            && courseLookupResult.Value.AuthorId == _user.UserId;
        if (!isPrivileged && !isAuthorOwner)
        {
            return Error.Authorization("review.not.authorized", "Нет прав на рецензирование в этом курсе");
        }

        Guid studentId = command.Request.UserId;

        if (targetStatus == IssueProgressStatus.COMPLETED)
        {
            // Reviewer обязателен только для COMPLETED (synthetic submission + ForceApprove). Та же
            // резолюция/fail-closed, что у MarkIssueCompleteForUser (#505).
            Guid reviewerId = _user.UserId != Guid.Empty
                ? _user.UserId
                : (isPrivileged && command.Request.ReviewerId is { } overrideReviewerId && overrideReviewerId != Guid.Empty
                    ? overrideReviewerId
                    : Guid.Empty);

            if (reviewerId == Guid.Empty)
            {
                return Error.Validation(
                    "progress.review.reviewer.required",
                    "Не удалось определить ревьюера: service-токен без userId должен передать ReviewerId");
            }

            return await _staffIssueCompletion.CompleteIssueForUserAsync(
                studentId,
                command.CourseId,
                command.IssueId,
                reviewerId,
                feedback,
                courseLookupResult.Value,
                cancellationToken);
        }

        return await ApplyNonCompletedStatusAsync(command, targetStatus, courseLookupResult.Value, cancellationToken);
    }

    /// <summary>
    ///     NOT_STARTED / IN_PROGRESS / UNDER_REVIEW / REQUESTED_CHANGES — без submission'а и approve.
    ///     Строит/обеспечивает progress-цепочку (как при первом engagement'е студента), затем применяет
    ///     staff-переход на IssueProgress. При уходе из COMPLETED поднимается IssueProgressReopenedEvent —
    ///     откат XP/project/module. Двухфазный flush: цепочку коммитим до перехода, чтобы reopen-каскад
    ///     (резолвит строки через DB GetByAsync) видел закоммиченные Added-сущности.
    /// </summary>
    private async Task<UnitResult<Error>> ApplyNonCompletedStatusAsync(
        SetIssueStatusForUserCommand command,
        IssueProgressStatus targetStatus,
        CourseDto course,
        CancellationToken cancellationToken)
    {
        Result<IssueDetailDto, Error> issueDetailResult = await _ecsClient
            .GetDetailIssueByIdAsync(command.IssueId, cancellationToken);
        if (issueDetailResult.IsFailure)
        {
            return issueDetailResult.Error;
        }

        Guid projectId = issueDetailResult.Value.ProjectId;

        Result<IssueDto, Error> issueLookupResult = await _ecsClient
            .GetIssueLookupAsync(projectId, command.IssueId, cancellationToken);
        if (issueLookupResult.IsFailure)
        {
            return issueLookupResult.Error;
        }

        Guid? moduleId = issueLookupResult.Value.ModuleId;

        Result<ProjectDto, Error> projectLookupResult = await _ecsClient
            .GetProjectLookupAsync(command.CourseId, projectId, cancellationToken);
        if (projectLookupResult.IsFailure)
        {
            return projectLookupResult.Error;
        }

        Result<CourseEnrollment, Error> enrollmentResult = await _enrollmentAnchorService
            .EnsureEnrollmentAsync(
                command.Request.UserId,
                command.CourseId,
                course.AuthorId,
                EnrollmentSource.ENGAGEMENT,
                cancellationToken);
        if (enrollmentResult.IsFailure)
        {
            return enrollmentResult.Error;
        }

        Guid enrollmentId = enrollmentResult.Value.Id;

        UnitResult<Error> ensureProjectProgressResult = await EnsureProjectProgressAsync(
            enrollmentId,
            projectId,
            projectLookupResult.Value.ProjectIssuesTotal,
            cancellationToken);
        if (ensureProjectProgressResult.IsFailure)
        {
            return ensureProjectProgressResult.Error;
        }

        if (moduleId is not null)
        {
            Result<ModuleDto, Error> moduleLookupResult = await _ecsClient
                .GetModuleLookupAsync(command.CourseId, moduleId.Value, cancellationToken);
            if (moduleLookupResult.IsFailure)
            {
                return moduleLookupResult.Error;
            }

            UnitResult<Error> ensureModuleProgressResult = await _moduleProgressService.EnsureModuleProgressAsync(
                enrollmentId,
                moduleId.Value,
                moduleLookupResult.Value.ModuleItemsTotal,
                cancellationToken);
            if (ensureModuleProgressResult.IsFailure)
            {
                return ensureModuleProgressResult.Error;
            }

            UnitResult<Error> ensureModuleItemProgressResult = await EnsureIssueModuleItemProgressAsync(
                enrollmentId,
                command.IssueId,
                moduleId.Value,
                cancellationToken);
            if (ensureModuleItemProgressResult.IsFailure)
            {
                return ensureModuleItemProgressResult.Error;
            }
        }

        Result<IssueProgress, Error> issueProgressResult = await _issueProgressRepository
            .GetByAsync(
                x => x.EnrollmentId == enrollmentId && x.IssueId == command.IssueId,
                cancellationToken);
        if (issueProgressResult.IsFailureExceptNotFound())
        {
            return issueProgressResult.Error;
        }

        IssueProgress issueProgress;
        if (issueProgressResult.IsNotFound())
        {
            Result<IssueProgress, Error> createIssueProgressResult = IssueProgress.Create(
                enrollmentId,
                projectId,
                command.IssueId);
            if (createIssueProgressResult.IsFailure)
            {
                return createIssueProgressResult.Error;
            }

            issueProgress = createIssueProgressResult.Value;
            await _issueProgressRepository.AddAsync(issueProgress, cancellationToken);
        }
        else
        {
            issueProgress = issueProgressResult.Value;
        }

        // Двухфазный flush: коммитим всю progress-цепочку (enrollment-anchor + Project/Module/Issue
        // progress) ДО применения перехода. При уходе из COMPLETED IssueProgress поднимает
        // IssueProgressReopenedEvent, чьи handler'ы (Revoke XP / Revert project / Uncomplete module)
        // резолвят строки через DB GetByAsync — без предварительного flush'а они не увидели бы свежие
        // Added-сущности (тот же паттерн, что в MarkIssueCompleteForUser). При не-reopen-кейсе это просто
        // лишний no-op flush.
        UnitResult<Error> chainSaveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (chainSaveResult.IsFailure)
        {
            return chainSaveResult.Error;
        }

        // Reset для NOT_STARTED, прямой staff-set для остальных. Reopened-event поднимается ⇔ уходим
        // из COMPLETED; идемпотентно (target == current → no-op).
        UnitResult<Error> transitionResult = targetStatus == IssueProgressStatus.NOT_STARTED
            ? issueProgress.Reset()
            : issueProgress.SetStatusByStaff(targetStatus);
        if (transitionResult.IsFailure)
        {
            return transitionResult.Error;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Issue progress status set by staff. UserId: {UserId}, ReviewerId: {ReviewerId}, CourseId: {CourseId}, IssueId: {IssueId}, TargetStatus: {TargetStatus}",
            command.Request.UserId,
            _user.UserId,
            command.CourseId,
            command.IssueId,
            targetStatus);

        return UnitResult.Success<Error>();
    }

    private async Task<UnitResult<Error>> EnsureProjectProgressAsync(
        Guid enrollmentId,
        Guid projectId,
        int projectIssuesTotal,
        CancellationToken cancellationToken)
    {
        bool alreadyExists = await _projectProgressRepository.ExistsAsync(
            p => p.EnrollmentId == enrollmentId && p.ProjectId == projectId,
            cancellationToken);

        if (alreadyExists)
        {
            return UnitResult.Success<Error>();
        }

        Result<ProjectProgress, Error> createProjectProgressResult = ProjectProgress.Create(
            enrollmentId,
            projectId,
            projectIssuesTotal);
        if (createProjectProgressResult.IsFailure)
        {
            return createProjectProgressResult.Error;
        }

        await _projectProgressRepository.AddAsync(createProjectProgressResult.Value, cancellationToken);

        return UnitResult.Success<Error>();
    }

    private async Task<UnitResult<Error>> EnsureIssueModuleItemProgressAsync(
        Guid enrollmentId,
        Guid issueId,
        Guid moduleId,
        CancellationToken cancellationToken)
    {
        bool alreadyExists = await _moduleItemProgressRepository.ExistsAsync(
            x => x.EnrollmentId == enrollmentId
                 && x.ModuleId == moduleId
                 && x.ReferenceId == issueId
                 && x.ItemType == ModuleItemProgressType.ISSUE,
            cancellationToken);

        if (alreadyExists)
        {
            return UnitResult.Success<Error>();
        }

        Result<ModuleItemProgress, Error> createModuleItemProgressResult = ModuleItemProgress.CreateIssueProgress(
            enrollmentId,
            moduleId,
            issueId);
        if (createModuleItemProgressResult.IsFailure)
        {
            return createModuleItemProgressResult.Error;
        }

        await _moduleItemProgressRepository.AddAsync(createModuleItemProgressResult.Value, cancellationToken);

        return UnitResult.Success<Error>();
    }
}
