using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Domain.Projects;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

public sealed record PublishIssueCommand(Guid IssueId, bool NotifySubscribers) : ICommand;

public sealed record PublishIssueRequest(bool NotifySubscribers = true);

public sealed class PublishIssueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("issues/{issueId:guid}/publish", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid issueId,
                    [FromBody] PublishIssueRequest? request,
                    [FromServices] PublishIssueHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new PublishIssueCommand(issueId, request?.NotifySubscribers ?? true),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class PublishIssueHandler : ICommandHandler<Guid, PublishIssueCommand>
{
    private readonly IIssuesRepository _issuesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly ILogger<PublishIssueHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public PublishIssueHandler(
        IIssuesRepository issuesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        ILogger<PublishIssueHandler> logger,
        UserScopedData userScopedData)
    {
        _issuesRepository = issuesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        PublishIssueCommand command, CancellationToken cancellationToken)
    {
        Result<Issue, Error> issueResult = await _issuesRepository.GetByAsync(
            i => i.Id == command.IssueId, cancellationToken);
        if (issueResult.IsFailure)
            return issueResult.Error;

        Issue issue = issueResult.Value;

        Guid? courseAuthorId = await _issuesRepository.GetCourseAuthorIdAsync(command.IssueId, cancellationToken);
        UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseAuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        UnitResult<Error> publishResult = issue.Publish();
        if (publishResult.IsFailure)
            return publishResult.Error;

        List<Guid> courseIds = await _issuesRepository.GetCourseIdsAsync(issue.Id, cancellationToken);

        await _outbox.PublishAsync(new IssuePublished(
            IssueId: issue.Id,
            ProjectId: issue.ProjectId,
            Title: issue.Title.Value,
            AuthorId: issue.AuthorId,
            CourseIds: courseIds,
            NotifySubscribers: command.NotifySubscribers));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Issue {IssueId} published", issue.Id);
        return issue.Id;
    }
}
