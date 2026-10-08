using Core.Abstractions;
using Core.Database;
using EducationContentService.Contracts.Issues;
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

public sealed record UpdateReviewSpecCommand(Guid IssueId, UpdateReviewSpecRequest Request) : ICommand;

public sealed class UpdateReviewSpecValidator : AbstractValidator<UpdateReviewSpecRequest>
{
    public UpdateReviewSpecValidator()
    {
        RuleFor(x => x.AuthorPrompt)
            .MaximumLength(ReviewSpec.AUTHOR_PROMPT_MAX_LENGTH);

        RuleFor(x => x.ReviewAspects)
            .MaximumLength(ReviewSpec.REVIEW_ASPECTS_MAX_LENGTH);
    }
}

public sealed class UpdateReviewSpecEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("issues/{issueId:guid}/review-spec",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid issueId,
                    [FromBody] UpdateReviewSpecRequest request,
                    [FromServices] UpdateReviewSpecHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new UpdateReviewSpecCommand(issueId, request), ct))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class UpdateReviewSpecHandler : ICommandHandler<Guid, UpdateReviewSpecCommand>
{
    private readonly IIssuesRepository _issues;
    private readonly IProjectsRepository _projects;
    private readonly IReviewConfigRepository _reviewConfig;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;
    private readonly IValidator<UpdateReviewSpecRequest> _validator;
    private readonly UserScopedData _user;

    public UpdateReviewSpecHandler(
        IIssuesRepository issues,
        IProjectsRepository projects,
        IReviewConfigRepository reviewConfig,
        ITransactionManager transactions,
        IOutboxService outbox,
        IValidator<UpdateReviewSpecRequest> validator,
        UserScopedData user)
    {
        _issues = issues;
        _projects = projects;
        _reviewConfig = reviewConfig;
        _transactions = transactions;
        _outbox = outbox;
        _validator = validator;
        _user = user;
    }

    public async Task<Result<Guid, Error>> Handle(UpdateReviewSpecCommand command, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validation =
            await _validator.ValidateAsync(command.Request, ct);
        if (!validation.IsValid)
            return Error.Validation("education.review_spec.invalid", validation.Errors[0].ErrorMessage);

        Result<Issue, Error> issueResult = await _issues.GetByAsync(i => i.Id == command.IssueId, ct);
        if (issueResult.IsFailure)
            return issueResult.Error;
        Issue issue = issueResult.Value;

        Result<Project, Error> projectResult = await _projects.GetByAsync(p => p.Id == issue.ProjectId, ct);
        if (projectResult.IsFailure)
            return projectResult.Error;
        Project project = projectResult.Value;

        UnitResult<Error> ownership = _user.CheckOwnership(project.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        ReviewSpec? existing = await _reviewConfig.GetReviewSpecAsync(
            s => s.IssueId == command.IssueId, ct);

        if (existing is null)
        {
            existing = ReviewSpec.Create(
                command.IssueId,
                issue.ProjectId,
                command.Request.AuthorPrompt,
                command.Request.ReviewAspects,
                command.Request.IsAutoReviewEnabled);
            await _reviewConfig.AddReviewSpecAsync(existing, ct);
        }
        else
        {
            existing.Update(
                command.Request.AuthorPrompt,
                command.Request.ReviewAspects,
                command.Request.IsAutoReviewEnabled);
        }

        await _outbox.PublishAsync(new ReviewSpecUpdated(
            command.IssueId,
            issue.ProjectId,
            project.AuthorId,
            existing.AuthorPrompt,
            existing.ReviewAspects));

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        return saveResult.IsFailure ? saveResult.Error : existing.Id;
    }
}
