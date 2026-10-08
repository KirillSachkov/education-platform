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
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Projects;

namespace ProgressService.Core.Features.Issues.UseCases;

public sealed record StartIssueWorkCommand(Guid CourseId, Guid ProjectId, Guid IssueId) : ICommand;

public sealed class StartIssueWorkCommandValidator : AbstractValidator<StartIssueWorkCommand>
{
    public StartIssueWorkCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartIssueWorkCommand.CourseId)));
        RuleFor(x => x.ProjectId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartIssueWorkCommand.ProjectId)));
        RuleFor(x => x.IssueId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartIssueWorkCommand.IssueId)));
    }
}

public sealed class StartIssueWorkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/courses/{courseId:guid}/projects/{projectId:guid}/issues/{issueId:guid}/start",
            async Task<EndpointResult> (
                    Guid courseId,
                    Guid projectId,
                    Guid issueId,
                    StartIssueWorkHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new StartIssueWorkCommand(courseId, projectId, issueId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class StartIssueWorkHandler : ICommandHandler<StartIssueWorkCommand>
{
    private readonly IEnrollmentAnchorService _enrollmentAnchorService;
    private readonly IProjectProgressRepository _projectProgressRepository;
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly IModuleItemProgressRepository _moduleItemProgressRepository;
    private readonly IModuleProgressService _moduleProgressService;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<StartIssueWorkCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<StartIssueWorkHandler> _logger;

    public StartIssueWorkHandler(
        IEnrollmentAnchorService enrollmentAnchorService,
        IProjectProgressRepository projectProgressRepository,
        IIssueProgressRepository issueProgressRepository,
        IModuleItemProgressRepository moduleItemProgressRepository,
        IModuleProgressService moduleProgressService,
        IEntitlementChecker entitlementChecker,
        IEducationContentServiceClient ecsClient,
        ITransactionManager transactionManager,
        IValidator<StartIssueWorkCommand> validator,
        UserScopedData user,
        ILogger<StartIssueWorkHandler> logger)
    {
        _enrollmentAnchorService = enrollmentAnchorService;
        _projectProgressRepository = projectProgressRepository;
        _issueProgressRepository = issueProgressRepository;
        _moduleItemProgressRepository = moduleItemProgressRepository;
        _moduleProgressService = moduleProgressService;
        _entitlementChecker = entitlementChecker;
        _ecsClient = ecsClient;
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(StartIssueWorkCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Entitlement-гейт (access-derive-model Phase 2): раньше гейтом было само наличие
        // enrollment'а; теперь enrollment ленивый, поэтому доступ к issue проверяем явно через
        // Redis SINTER — иначе неавторизованный пользователь создал бы anchor «по требованию».
        AccessDecision accessDecision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            ResourceTypes.ISSUE,
            command.IssueId,
            cancellationToken);
        if (!accessDecision.IsGranted)
        {
            return ProgressErrors.IssueLockedForTrial();
        }

        Result<IssueDto, Error> issueContextResult = await _ecsClient
            .GetIssueLookupAsync(command.ProjectId, command.IssueId, cancellationToken);
        if (issueContextResult.IsFailure)
        {
            return issueContextResult.Error;
        }

        Result<ProjectDto, Error> projectContextResult = await _ecsClient
            .GetProjectLookupAsync(command.CourseId, command.ProjectId, cancellationToken);
        if (projectContextResult.IsFailure)
        {
            return projectContextResult.Error;
        }

        // Lazy progress-anchor: ensure-create вместо NotFound. authorId резолвим из ECS.
        Result<CourseDto, Error> courseLookupResult = await _ecsClient
            .GetCourseLookupAsync(command.CourseId, cancellationToken);
        if (courseLookupResult.IsFailure)
        {
            return courseLookupResult.Error;
        }

        Result<CourseEnrollment, Error> enrollmentResult = await _enrollmentAnchorService
            .EnsureEnrollmentAsync(
                _user.UserId,
                command.CourseId,
                courseLookupResult.Value.AuthorId,
                EnrollmentSource.ENGAGEMENT,
                cancellationToken);
        if (enrollmentResult.IsFailure)
        {
            return enrollmentResult.Error;
        }

        CourseEnrollment enrollment = enrollmentResult.Value;
        IssueDto issueContext = issueContextResult.Value;

        UnitResult<Error> ensureProjectProgressResult = await EnsureProjectProgressAsync(
            enrollment.Id,
            command.ProjectId,
            projectContextResult.Value.ProjectIssuesTotal,
            cancellationToken);
        if (ensureProjectProgressResult.IsFailure)
        {
            return ensureProjectProgressResult.Error;
        }

        if (issueContext.ModuleId is not null)
        {
            Result<ModuleDto, Error> moduleContextResult = await _ecsClient
                .GetModuleLookupAsync(command.CourseId, issueContext.ModuleId.Value, cancellationToken);
            if (moduleContextResult.IsFailure)
            {
                return moduleContextResult.Error;
            }

            UnitResult<Error> ensureModuleProgressResult = await _moduleProgressService.EnsureModuleProgressAsync(
                enrollment.Id,
                issueContext.ModuleId.Value,
                moduleContextResult.Value.ModuleItemsTotal,
                cancellationToken);
            if (ensureModuleProgressResult.IsFailure)
            {
                return ensureModuleProgressResult.Error;
            }
        }

        Result<IssueProgress, Error> issueProgressResult = await _issueProgressRepository
            .GetByAsync(
                x => x.EnrollmentId == enrollment.Id && x.IssueId == command.IssueId,
                cancellationToken);
        if (issueProgressResult.IsFailureExceptNotFound())
        {
            return issueProgressResult.Error;
        }

        if (issueProgressResult.IsNotFound())
        {
            Result<IssueProgress, Error> createIssueProgressResult = IssueProgress.Create(
                enrollment.Id,
                command.ProjectId,
                command.IssueId);
            if (createIssueProgressResult.IsFailure)
            {
                return createIssueProgressResult.Error;
            }

            issueProgressResult = createIssueProgressResult;
            await _issueProgressRepository.AddAsync(issueProgressResult.Value, cancellationToken);
        }

        if (issueProgressResult.Value.Status == IssueProgressStatus.NOT_STARTED ||
            issueProgressResult.Value.Status == IssueProgressStatus.IN_PROGRESS)
        {
            UnitResult<Error> startWorkResult = issueProgressResult.Value.StartWork();
            if (startWorkResult.IsFailure)
            {
                return startWorkResult.Error;
            }
        }

        UnitResult<Error> createModuleItemProgressResult = await CreateModuleItemProgressIfNeededAsync(
            enrollment.Id,
            command.IssueId,
            issueContext.ModuleId,
            cancellationToken);
        if (createModuleItemProgressResult.IsFailure)
        {
            return createModuleItemProgressResult.Error;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Issue work started successfully. UserId: {UserId}, CourseId: {CourseId}, ProjectId: {ProjectId}, IssueId: {IssueId}",
            _user.UserId,
            command.CourseId,
            command.ProjectId,
            command.IssueId);

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

    private async Task<UnitResult<Error>> CreateModuleItemProgressIfNeededAsync(
        Guid enrollmentId,
        Guid issueId,
        Guid? moduleId,
        CancellationToken cancellationToken)
    {
        if (moduleId is null)
        {
            return UnitResult.Success<Error>();
        }

        Guid targetModuleId = moduleId.Value;
        bool alreadyExists = await _moduleItemProgressRepository.ExistsAsync(
            x => x.EnrollmentId == enrollmentId
                 && x.ModuleId == targetModuleId
                 && x.ReferenceId == issueId
                 && x.ItemType == ModuleItemProgressType.ISSUE,
            cancellationToken);

        if (alreadyExists)
        {
            return UnitResult.Success<Error>();
        }

        Result<ModuleItemProgress, Error> createModuleItemProgressResult = ModuleItemProgress.CreateIssueProgress(
            enrollmentId,
            moduleId.Value,
            issueId);
        if (createModuleItemProgressResult.IsFailure)
        {
            return createModuleItemProgressResult.Error;
        }

        await _moduleItemProgressRepository.AddAsync(createModuleItemProgressResult.Value, cancellationToken);

        return UnitResult.Success<Error>();
    }
}
