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
using ProgressService.Domain;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Projects;

namespace ProgressService.Core.Features.Projects.UseCases;

public sealed record StartProjectWorkCommand(Guid CourseId, Guid ProjectId) : ICommand;

public sealed class StartProjectWorkCommandValidator : AbstractValidator<StartProjectWorkCommand>
{
    public StartProjectWorkCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartProjectWorkCommand.CourseId)));
        RuleFor(x => x.ProjectId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartProjectWorkCommand.ProjectId)));
    }
}

public sealed class StartProjectWorkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/courses/{courseId:guid}/projects/{projectId:guid}/start", async Task<EndpointResult> (
                Guid courseId,
                Guid projectId,
                StartProjectWorkHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new StartProjectWorkCommand(courseId, projectId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class StartProjectWorkHandler : ICommandHandler<StartProjectWorkCommand>
{
    private readonly IEnrollmentAnchorService _enrollmentAnchorService;
    private readonly IProjectProgressRepository _projectProgressRepository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<StartProjectWorkCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<StartProjectWorkHandler> _logger;

    public StartProjectWorkHandler(
        IEnrollmentAnchorService enrollmentAnchorService,
        IProjectProgressRepository projectProgressRepository,
        IEntitlementChecker entitlementChecker,
        IEducationContentServiceClient ecsClient,
        ITransactionManager transactionManager,
        IValidator<StartProjectWorkCommand> validator,
        UserScopedData user,
        ILogger<StartProjectWorkHandler> logger)
    {
        _enrollmentAnchorService = enrollmentAnchorService;
        _projectProgressRepository = projectProgressRepository;
        _entitlementChecker = entitlementChecker;
        _ecsClient = ecsClient;
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(StartProjectWorkCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Entitlement-гейт (access-derive-model Phase 2): enrollment стал ленивым, поэтому
        // доступ к курсу проверяем явно через Redis SINTER. Без этого начать работу над проектом
        // (и материализовать anchor) мог бы пользователь без grant'а.
        AccessDecision accessDecision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            ResourceTypes.COURSE,
            command.CourseId,
            cancellationToken);
        if (!accessDecision.IsGranted)
        {
            return ProgressErrors.MaterialAccessDenied();
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

        Guid enrollmentId = enrollmentResult.Value.Id;
        Guid projectId = command.ProjectId;
        bool alreadyExists = await _projectProgressRepository
            .ExistsAsync(
                p => p.EnrollmentId == enrollmentId && p.ProjectId == projectId,
                cancellationToken);

        if (alreadyExists)
        {
            return UnitResult.Success<Error>();
        }

        Result<ProjectProgress, Error> createProjectProgressResult = ProjectProgress.Create(
            enrollmentId,
            command.ProjectId,
            projectContextResult.Value.ProjectIssuesTotal);
        if (createProjectProgressResult.IsFailure)
        {
            return createProjectProgressResult.Error;
        }

        await _projectProgressRepository.AddAsync(createProjectProgressResult.Value, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Project work started successfully. UserId: {UserId}, CourseId: {CourseId}, ProjectId: {ProjectId}",
            _user.UserId,
            command.CourseId,
            command.ProjectId);

        return UnitResult.Success<Error>();
    }
}
