using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Issues;
using EducationContentService.Domain;
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

public sealed record UpdateIssueCommand(Guid IssueId, UpdateIssueRequest Request) : ICommand;

public class UpdateIssueRequestValidator : AbstractValidator<UpdateIssueRequest>
{
    public UpdateIssueRequestValidator()
    {
        RuleFor(x => x.Title).MustBeValueObject(Title.Create);
        RuleFor(x => x.Content).MustBeValueObject(MarkdownContent.Create);
        RuleFor(x => x.AccessType)
            .Must(value => Enum.TryParse<AccessType>(value, true, out _))
            .WithError(EducationErrors.InvalidAccessType());
        RuleFor(x => x.SubmissionMode)
            .Must(value => Enum.TryParse<IssueSubmissionMode>(value, true, out _))
            .WithMessage("Неверный режим сдачи задачи.");
        RuleFor(x => x.SelfCheckInstructions)
            .MaximumLength(MarkdownContent.MAX_LENGTH)
            .When(x => !string.IsNullOrWhiteSpace(x.SelfCheckInstructions));
    }
}

public sealed class UpdateIssueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("issues/{issueId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid issueId,
                    [FromBody] UpdateIssueRequest request,
                    [FromServices] UpdateIssueHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new UpdateIssueCommand(issueId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class UpdateIssueHandler : ICommandHandler<Guid, UpdateIssueCommand>
{
    private readonly IIssuesRepository _issuesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<UpdateIssueRequest> _validator;
    private readonly ILogger<UpdateIssueHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public UpdateIssueHandler(
        IIssuesRepository issuesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<UpdateIssueRequest> validator,
        ILogger<UpdateIssueHandler> logger,
        UserScopedData userScopedData)
    {
        _issuesRepository = issuesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateIssueCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
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

        Title title = Title.Create(command.Request.Title).Value;
        MarkdownContent content = MarkdownContent.Create(command.Request.Content).Value;
        AccessType accessType = Enum.Parse<AccessType>(command.Request.AccessType, true);
        IssueSubmissionMode submissionMode =
            Enum.Parse<IssueSubmissionMode>(command.Request.SubmissionMode, true);
        bool accessTypeChanged = issue.AccessType != accessType;

        issue.Update(
            title,
            content,
            accessType,
            submissionMode,
            command.Request.SelfCheckInstructions);

        await _outbox.PublishAsync(new IssueUpdated(issue.Id));
        if (accessTypeChanged)
            await _outbox.PublishAsync(new IssueAccessChanged(issue.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Issue {IssueId} updated", command.IssueId);

        return issue.Id;
    }
}
