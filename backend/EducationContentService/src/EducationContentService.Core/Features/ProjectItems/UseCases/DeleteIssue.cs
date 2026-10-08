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

public sealed record DeleteIssueCommand(Guid IssueId) : ICommand;

public sealed class DeleteIssueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("issues/{issueId:guid}", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid issueId,
                [FromServices] DeleteIssueHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new DeleteIssueCommand(issueId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class DeleteIssueHandler : ICommandHandler<Guid, DeleteIssueCommand>
{
    private readonly IIssuesRepository _issuesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<DeleteIssueHandler> _logger;

    public DeleteIssueHandler(
        IIssuesRepository issuesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        UserScopedData userScopedData,
        ILogger<DeleteIssueHandler> logger)
    {
        _issuesRepository = issuesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        DeleteIssueCommand command, CancellationToken cancellationToken)
    {
        Result<Issue, Error> issueResult = await _issuesRepository.GetByAsync(
            i => i.Id == command.IssueId, cancellationToken);
        if (issueResult.IsFailure)
            return issueResult.Error;

        // Owner-or-admin via the issue's course author (null → admin-only for orphans).
        Guid? courseAuthorId = await _issuesRepository.GetCourseAuthorIdAsync(
            command.IssueId, cancellationToken);
        UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseAuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Issue issue = issueResult.Value;

        _issuesRepository.Delete(issue);

        await _outbox.PublishAsync(new IssueHardDeleted(issue.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Issue {IssueId} hard-deleted", issue.Id);

        return issue.Id;
    }
}
