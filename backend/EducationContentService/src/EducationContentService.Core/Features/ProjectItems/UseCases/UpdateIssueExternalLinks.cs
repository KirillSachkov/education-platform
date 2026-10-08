using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Issues;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.Projects.ValueObjects;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

public sealed record UpdateIssueExternalLinksCommand(
    Guid IssueId,
    UpdateIssueExternalLinksRequest Request) : ICommand;

public class UpdateIssueExternalLinksRequestValidator
    : AbstractValidator<UpdateIssueExternalLinksRequest>
{
    public UpdateIssueExternalLinksRequestValidator()
    {
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.Url)
                .NotEmpty()
                .WithMessage("URL ссылки не может быть пустым");

            item.RuleFor(x => x.Url)
                .MaximumLength(2048)
                .WithMessage("URL ссылки не может превышать 2048 символов");

            item.RuleFor(x => x.Title)
                .NotEmpty()
                .WithMessage("Название ссылки не может быть пустым");

            item.RuleFor(x => x.Title)
                .MaximumLength(200)
                .WithMessage("Название ссылки не может превышать 200 символов");
        });
    }
}

public sealed class UpdateIssueExternalLinksEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("issues/{issueId:guid}/external-links", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid issueId,
                    [FromBody] UpdateIssueExternalLinksRequest request,
                    [FromServices] UpdateIssueExternalLinksHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new UpdateIssueExternalLinksCommand(issueId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class UpdateIssueExternalLinksHandler
    : ICommandHandler<Guid, UpdateIssueExternalLinksCommand>
{
    private readonly IIssuesRepository _issuesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<UpdateIssueExternalLinksRequest> _validator;
    private readonly ILogger<UpdateIssueExternalLinksHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public UpdateIssueExternalLinksHandler(
        IIssuesRepository issuesRepository,
        ITransactionManager transactionManager,
        IValidator<UpdateIssueExternalLinksRequest> validator,
        ILogger<UpdateIssueExternalLinksHandler> logger,
        UserScopedData userScopedData)
    {
        _issuesRepository = issuesRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateIssueExternalLinksCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(
            command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Issue, Error> issueResult = await _issuesRepository.GetByAsync(
            i => i.Id == command.IssueId, cancellationToken);
        if (issueResult.IsFailure)
            return issueResult.Error;

        Issue issue = issueResult.Value;

        Guid? courseAuthorId = await _issuesRepository.GetCourseAuthorIdAsync(command.IssueId, cancellationToken);
        UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseAuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        var links = new List<IssueExternalLink>();
        foreach (var item in command.Request.Items)
        {
            Result<IssueExternalLink, Error> linkResult =
                IssueExternalLink.Create(item.Url, item.Title, item.IsRequired);
            if (linkResult.IsFailure)
                return linkResult.Error;

            links.Add(linkResult.Value);
        }

        issue.UpdateExternalLinks(links);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Issue {IssueId} external links updated ({Count} items)",
            command.IssueId, links.Count);

        return issue.Id;
    }
}
