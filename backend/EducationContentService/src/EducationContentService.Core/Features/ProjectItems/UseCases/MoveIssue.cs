using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Projects;
using EducationContentService.Domain.Projects;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

public sealed record MoveProjectIssueCommand(
    Guid ProjectId, Guid IssueId, MoveProjectIssueRequest Request) : ICommand;

public class MoveProjectIssueRequestValidator : AbstractValidator<MoveProjectIssueRequest>
{
    public MoveProjectIssueRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.AfterSortKey is not null || x.BeforeSortKey is not null)
            .WithError(GeneralErrors.ValueIsInvalid("AfterSortKey/BeforeSortKey"));
    }
}

public sealed class MoveProjectIssueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("projects/{projectId:guid}/issues/{issueId:guid}/move",
            async Task<EndpointResult<Guid>> (
                [FromRoute] Guid projectId,
                [FromRoute] Guid issueId,
                [FromBody] MoveProjectIssueRequest request,
                [FromServices] MoveProjectIssueHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new MoveProjectIssueCommand(projectId, issueId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class MoveProjectIssueHandler : ICommandHandler<Guid, MoveProjectIssueCommand>
{
    private readonly IProjectItemsRepository _projectItemsRepository;
    private readonly IProjectsRepository _projectsRepository;
    private readonly ProjectItemService _projectItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<MoveProjectIssueRequest> _validator;
    private readonly ILogger<MoveProjectIssueHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public MoveProjectIssueHandler(
        IProjectItemsRepository projectItemsRepository,
        IProjectsRepository projectsRepository,
        ProjectItemService projectItemService,
        ITransactionManager transactionManager,
        IValidator<MoveProjectIssueRequest> validator,
        ILogger<MoveProjectIssueHandler> logger,
        UserScopedData userScopedData)
    {
        _projectItemsRepository = projectItemsRepository;
        _projectsRepository = projectsRepository;
        _projectItemService = projectItemService;
        _transactionManager = transactionManager;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        MoveProjectIssueCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Project, Error> projectResult = await _projectsRepository.GetByAsync(
            p => p.Id == command.ProjectId, cancellationToken);
        if (projectResult.IsFailure)
            return projectResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(projectResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Result<ProjectItem, Error> itemResult = await _projectItemsRepository.GetByAsync(
            pi => pi.ProjectId == command.ProjectId && pi.IssueId == command.IssueId,
            cancellationToken: cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        ProjectItem item = itemResult.Value;

        Result<SortKey, Error> sortKeyResult = await _projectItemService.ComputeMoveSortKey(
            command.ProjectId, command.IssueId,
            command.Request.AfterSortKey, command.Request.BeforeSortKey,
            cancellationToken);
        if (sortKeyResult.IsFailure)
            return sortKeyResult.Error;

        item.UpdateSortKey(sortKeyResult.Value);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Issue {IssueId} moved in project {ProjectId}",
            command.IssueId, command.ProjectId);

        return item.Id;
    }
}
