using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Core.Features.ProjectItems;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ModuleItems.UseCases;

public sealed record AttachIssueToModuleCommand(Guid ModuleId, Guid IssueId) : ICommand;

public sealed class AttachIssueToModuleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("modules/{moduleId:guid}/issues/{issueId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid moduleId,
                    [FromRoute] Guid issueId,
                    [FromServices] AttachIssueToModuleHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new AttachIssueToModuleCommand(moduleId, issueId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Modules.MANAGE);
    }
}

public sealed class AttachIssueToModuleHandler : ICommandHandler<Guid, AttachIssueToModuleCommand>
{
    private readonly IModulesRepository _modulesRepository;
    private readonly IIssuesRepository _issuesRepository;
    private readonly ICoursesRepository _coursesRepository;
    private readonly ModuleItemService _moduleItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly ILogger<AttachIssueToModuleHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public AttachIssueToModuleHandler(
        IModulesRepository modulesRepository,
        IIssuesRepository issuesRepository,
        ICoursesRepository coursesRepository,
        ModuleItemService moduleItemService,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        ILogger<AttachIssueToModuleHandler> logger,
        UserScopedData userScopedData)
    {
        _modulesRepository = modulesRepository;
        _issuesRepository = issuesRepository;
        _coursesRepository = coursesRepository;
        _moduleItemService = moduleItemService;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        AttachIssueToModuleCommand command, CancellationToken cancellationToken)
    {
        Result<Module, Error> moduleResult = await _modulesRepository.GetByAsync(
            m => m.Id == command.ModuleId, cancellationToken);
        if (moduleResult.IsFailure)
            return moduleResult.Error;

        UnitResult<Error> moduleOwnership = _userScopedData.CheckOwnership(moduleResult.Value.AuthorId);
        if (moduleOwnership.IsFailure)
            return moduleOwnership.Error;

        Result<Issue, Error> issueResult = await _issuesRepository.GetByAsync(
            i => i.Id == command.IssueId, cancellationToken);
        if (issueResult.IsFailure)
            return issueResult.Error;

        UnitResult<Error> issueOwnership = _userScopedData.CheckOwnership(issueResult.Value.AuthorId);
        if (issueOwnership.IsFailure)
            return issueOwnership.Error;

        // Интенсивы и марафоны не содержат заданий: если модуль уже прикреплён к такому курсу —
        // attach запрещаем (issue #291 для INTENSIVE, #374 для MARATHON). Orphan-модуль (не
        // прикреплён ни к одному курсу) получает разрешение по умолчанию: «принадлежность» курсу
        // определяется только через course_items, и если этой связи нет, ограничение неприменимо.
        // `GetCourseIdAsync` возвращает первый найденный course_items.course_id — модель
        // допускает только одну привязку модуля к курсу, поэтому проверка одного достаточно.
        Guid? courseId = await _modulesRepository.GetCourseIdAsync(command.ModuleId, cancellationToken);
        if (courseId is not null)
        {
            Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
                c => c.Id == courseId.Value, cancellationToken);
            if (courseResult.IsSuccess)
            {
                if (courseResult.Value.Kind == CourseKind.INTENSIVE)
                    return EducationErrors.IssuesNotAllowedInIntensive();
                if (courseResult.Value.Kind == CourseKind.MARATHON)
                    return EducationErrors.IssuesNotAllowedInMarathon();
            }
        }

        Result<ModuleItem, Error> itemResult = await _moduleItemService.CreateAsync(
            command.ModuleId, ModuleItemType.Issue, command.IssueId, cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        await _outbox.PublishAsync(new IssueAccessChanged(command.IssueId));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Issue {IssueId} bound to module {ModuleId}",
            command.IssueId, command.ModuleId);

        return itemResult.Value.Id;
    }
}
