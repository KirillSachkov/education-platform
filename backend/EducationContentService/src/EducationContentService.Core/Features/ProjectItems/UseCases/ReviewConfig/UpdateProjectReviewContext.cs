using Core.Abstractions;
using Core.Database;
using EducationContentService.Contracts.Projects;
using EducationContentService.Core.Database;
using EducationContentService.Domain.Projects;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ProjectItems.UseCases.ReviewConfig;

public sealed record UpdateProjectReviewContextCommand(
    Guid ProjectId,
    UpdateProjectReviewContextRequest Request) : ICommand;

public sealed class UpdateProjectReviewContextValidator
    : AbstractValidator<UpdateProjectReviewContextRequest>
{
    public UpdateProjectReviewContextValidator()
    {
        RuleFor(x => x.GuidelinesMarkdown)
            .NotNull()
            .WithMessage("GuidelinesMarkdown не может быть null (используйте пустую строку).")
            .MaximumLength(ProjectReviewContext.GUIDELINES_MAX_LENGTH);
        When(x => x.IsAutoReviewEnabled, () =>
        {
            RuleFor(x => x.RequiresGithubConnection)
                .Equal(true)
                .WithMessage("AI-проверка требует привязку GitHub.");
            RuleFor(x => x.RequiresReviewApp)
                .Equal(true)
                .WithMessage("AI-проверка требует GitHub App / review bot.");
        });
    }
}

public sealed class UpdateProjectReviewContextEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("projects/{projectId:guid}/review-context",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid projectId,
                    [FromBody] UpdateProjectReviewContextRequest request,
                    [FromServices] UpdateProjectReviewContextHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new UpdateProjectReviewContextCommand(projectId, request), ct))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class UpdateProjectReviewContextHandler
    : ICommandHandler<Guid, UpdateProjectReviewContextCommand>
{
    private readonly IProjectsRepository _projects;
    private readonly IReviewConfigRepository _reviewConfig;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;
    private readonly IValidator<UpdateProjectReviewContextRequest> _validator;
    private readonly UserScopedData _user;

    public UpdateProjectReviewContextHandler(
        IProjectsRepository projects,
        IReviewConfigRepository reviewConfig,
        ITransactionManager transactions,
        IOutboxService outbox,
        IValidator<UpdateProjectReviewContextRequest> validator,
        UserScopedData user)
    {
        _projects = projects;
        _reviewConfig = reviewConfig;
        _transactions = transactions;
        _outbox = outbox;
        _validator = validator;
        _user = user;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateProjectReviewContextCommand command, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validation =
            await _validator.ValidateAsync(command.Request, ct);
        if (!validation.IsValid)
            return Error.Validation(
                "education.review_context.invalid",
                validation.Errors[0].ErrorMessage);

        Result<Project, Error> projectResult = await _projects.GetByAsync(p => p.Id == command.ProjectId, ct);
        if (projectResult.IsFailure)
            return projectResult.Error;
        Project project = projectResult.Value;

        UnitResult<Error> ownership = _user.CheckOwnership(project.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        ProjectReviewContext? existing = await _reviewConfig.GetProjectReviewContextAsync(
            c => c.ProjectId == command.ProjectId, ct);

        if (existing is null)
        {
            existing = ProjectReviewContext.Create(
                command.ProjectId,
                command.Request.GuidelinesMarkdown,
                command.Request.IsAutoReviewEnabled,
                command.Request.RequiresGithubConnection,
                command.Request.RequiresReviewApp);
            await _reviewConfig.AddProjectReviewContextAsync(existing, ct);
        }
        else
        {
            existing.Update(
                command.Request.GuidelinesMarkdown,
                command.Request.IsAutoReviewEnabled,
                command.Request.RequiresGithubConnection,
                command.Request.RequiresReviewApp);
        }

        await _outbox.PublishAsync(new ProjectReviewContextUpdated(
            command.ProjectId,
            project.AuthorId,
            existing.GuidelinesMarkdown));

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        return saveResult.IsFailure ? saveResult.Error : existing.Id;
    }
}
