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

public sealed record RestoreIssueCommand(Guid IssueId) : ICommand;

public sealed class RestoreIssueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("issues/{issueId:guid}/restore", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid issueId,
                    [FromServices] RestoreIssueHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new RestoreIssueCommand(issueId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class RestoreIssueHandler : ICommandHandler<Guid, RestoreIssueCommand>
{
    private readonly IIssuesRepository _issuesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly ILogger<RestoreIssueHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public RestoreIssueHandler(
        IIssuesRepository issuesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        ILogger<RestoreIssueHandler> logger,
        UserScopedData userScopedData)
    {
        _issuesRepository = issuesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        RestoreIssueCommand command, CancellationToken cancellationToken)
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

        UnitResult<Error> restoreResult = issue.Restore();
        if (restoreResult.IsFailure)
            return restoreResult.Error;

        await _outbox.PublishAsync(new IssueRestored(issue.Id, Guid.Empty));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Issue {IssueId} restored from archive", issue.Id);

        return issue.Id;
    }
}
