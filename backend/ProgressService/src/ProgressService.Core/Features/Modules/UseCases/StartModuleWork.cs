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
using ProgressService.Domain.Modules;

namespace ProgressService.Core.Features.Modules.UseCases;

public sealed record StartModuleWorkCommand(Guid CourseId, Guid ModuleId) : ICommand;

public sealed class StartModuleWorkCommandValidator : AbstractValidator<StartModuleWorkCommand>
{
    public StartModuleWorkCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartModuleWorkCommand.CourseId)));
        RuleFor(x => x.ModuleId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartModuleWorkCommand.ModuleId)));
    }
}

public sealed class StartModuleWorkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/courses/{courseId:guid}/modules/{moduleId:guid}/start", async Task<EndpointResult> (
                Guid courseId,
                Guid moduleId,
                StartModuleWorkHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new StartModuleWorkCommand(courseId, moduleId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class StartModuleWorkHandler : ICommandHandler<StartModuleWorkCommand>
{
    private readonly IEnrollmentAnchorService _enrollmentAnchorService;
    private readonly IModuleProgressRepository _moduleProgressRepository;
    private readonly IModuleItemProgressRepository _moduleItemProgressRepository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<StartModuleWorkCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<StartModuleWorkHandler> _logger;

    public StartModuleWorkHandler(
        IEnrollmentAnchorService enrollmentAnchorService,
        IModuleProgressRepository moduleProgressRepository,
        IModuleItemProgressRepository moduleItemProgressRepository,
        IEntitlementChecker entitlementChecker,
        IEducationContentServiceClient ecsClient,
        ITransactionManager transactionManager,
        IValidator<StartModuleWorkCommand> validator,
        UserScopedData user,
        ILogger<StartModuleWorkHandler> logger)
    {
        _enrollmentAnchorService = enrollmentAnchorService;
        _moduleProgressRepository = moduleProgressRepository;
        _moduleItemProgressRepository = moduleItemProgressRepository;
        _entitlementChecker = entitlementChecker;
        _ecsClient = ecsClient;
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(StartModuleWorkCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Entitlement-гейт (access-derive-model Phase 2): enrollment стал ленивым, поэтому
        // доступ к курсу проверяем явно через Redis SINTER. Без этого начать работу над модулем
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

        Result<ModuleDto, Error> moduleContextResult = await _ecsClient
            .GetModuleLookupAsync(command.CourseId, command.ModuleId, cancellationToken);
        if (moduleContextResult.IsFailure)
        {
            return moduleContextResult.Error;
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
        Guid moduleId = command.ModuleId;
        bool alreadyExists = await _moduleProgressRepository
            .ExistsAsync(
                p => p.EnrollmentId == enrollmentId && p.ModuleId == moduleId,
                cancellationToken);

        if (alreadyExists)
        {
            return UnitResult.Success<Error>();
        }

        Result<ModuleProgress, Error> createModuleProgressResult = ModuleProgress.Create(
            enrollmentResult.Value.Id,
            command.ModuleId,
            moduleContextResult.Value.ModuleItemsTotal);
        if (createModuleProgressResult.IsFailure)
        {
            return createModuleProgressResult.Error;
        }

        ModuleProgress moduleProgress = createModuleProgressResult.Value;

        int completedModuleItemsCount = await _moduleItemProgressRepository
            .CountCompletedByEnrollmentAndModuleAsync(enrollmentId, moduleId, cancellationToken);

        //нужен для синхронизации когда мы в проекте сделали задачу а в модуле она еще не сделана.
        for (int index = 0; index < completedModuleItemsCount; index++)
        {
            UnitResult<Error> markItemCompletedResult = moduleProgress.MarkItemCompleted();
            if (markItemCompletedResult.IsFailure)
            {
                return markItemCompletedResult.Error;
            }
        }

        UnitResult<Error> tryCompleteModuleResult = moduleProgress.TryCompleteModule();
        if (tryCompleteModuleResult.IsFailure)
        {
            return tryCompleteModuleResult.Error;
        }

        await _moduleProgressRepository.AddAsync(moduleProgress, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Module work started successfully. UserId: {UserId}, CourseId: {CourseId}, ModuleId: {ModuleId}",
            _user.UserId,
            command.CourseId,
            command.ModuleId);

        return UnitResult.Success<Error>();
    }
}
