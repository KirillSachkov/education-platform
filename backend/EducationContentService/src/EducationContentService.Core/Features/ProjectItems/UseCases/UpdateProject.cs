using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Projects;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

public sealed record UpdateProjectCommand(Guid ProjectId, UpdateProjectRequest Request) : ICommand;

public class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Title).MustBeValueObject(Title.Create);
        RuleFor(x => x.Description).MustBeValueObject(v => Description.Create(v!))
            .When(x => !string.IsNullOrWhiteSpace(x.Description));
        When(x => x.DetailedDescription is not null, () =>
        {
            RuleFor(x => x.DetailedDescription!)
                .MustBeValueObject(DetailedDescription.Create);
        });
    }
}

public sealed class UpdateProjectEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("projects/{projectId:guid}", async Task<EndpointResult<Guid>> (
            [FromRoute] Guid projectId,
            [FromBody] UpdateProjectRequest request,
            [FromServices] UpdateProjectHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new UpdateProjectCommand(projectId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class UpdateProjectHandler : ICommandHandler<Guid, UpdateProjectCommand>
{
    private readonly IProjectsRepository _projectsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<UpdateProjectRequest> _validator;
    private readonly ILogger<UpdateProjectHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public UpdateProjectHandler(
        IProjectsRepository projectsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<UpdateProjectRequest> validator,
        ILogger<UpdateProjectHandler> logger,
        UserScopedData userScopedData)
    {
        _projectsRepository = projectsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateProjectCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Project, Error> projectResult = await _projectsRepository.GetByAsync(
            p => p.Id == command.ProjectId, cancellationToken);
        if (projectResult.IsFailure)
            return projectResult.Error;

        Project project = projectResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(project.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Title title = Title.Create(command.Request.Title).Value;
        Description? description = string.IsNullOrWhiteSpace(command.Request.Description)
            ? null
            : Description.Create(command.Request.Description).Value;
        DetailedDescription? detailedDescription = string.IsNullOrWhiteSpace(command.Request.DetailedDescription)
            ? null
            : DetailedDescription.Create(command.Request.DetailedDescription).Value;

        project.Update(title, description, detailedDescription);

        await _outbox.PublishAsync(new ProjectUpdated(project.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Project {ProjectId} updated", command.ProjectId);

        return project.Id;
    }
}
