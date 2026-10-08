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

public sealed record ArchiveIssueCommand(Guid IssueId) : ICommand;

public sealed class ArchiveIssueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("issues/{issueId:guid}/archive", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid issueId,
                    [FromServices] ArchiveIssueHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new ArchiveIssueCommand(issueId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class ArchiveIssueHandler : ICommandHandler<Guid, ArchiveIssueCommand>
{
    private readonly IIssuesRepository _issuesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly ILogger<ArchiveIssueHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public ArchiveIssueHandler(
        IIssuesRepository issuesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        ILogger<ArchiveIssueHandler> logger,
        UserScopedData userScopedData)
    {
        _issuesRepository = issuesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        ArchiveIssueCommand command, CancellationToken cancellationToken)
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

        UnitResult<Error> archiveResult = issue.Archive();
        if (archiveResult.IsFailure)
            return archiveResult.Error;

        await _outbox.PublishAsync(new IssueSoftDeleted(issue.Id, Guid.Empty));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Issue {IssueId} archived", issue.Id);
        return issue.Id;
    }
}
