using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.Progress.Events;
using SharedKernel;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>progress.events / issue_submission.changes_requested</c> → студенту.
/// URL строит dispatcher через <c>PlatformLinkBuilder</c> как <c>{openUrl}</c>.
/// </summary>
public sealed class IssueSubmissionChangesRequestedHandler
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<IssueSubmissionChangesRequestedHandler> _logger;

    public IssueSubmissionChangesRequestedHandler(
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        ILogger<IssueSubmissionChangesRequestedHandler> logger)
    {
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(IssueSubmissionChangesRequested evt, CancellationToken ct)
    {
        Task<string> issueTitleTask = ResolveIssueTitleAsync(evt.IssueId, ct);
        Task<CourseRouteContext> courseTask = NotificationRouteContextResolver.ResolveCourseAsync(
            _ecsClient,
            _authClient,
            evt.CourseId,
            _logger,
            ct);

        await Task.WhenAll(issueTitleTask, courseTask);

        string issueTitle = await issueTitleTask;
        CourseRouteContext course = await courseTask;

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.IssueSubmissionChangesRequested,
            recipientUserId: evt.UserId,
            correlationId: evt.SubmissionId,
            args: TemplateArgs.Of(
                ("issueTitle", issueTitle),
                ("reviewerComment", evt.Comment)),
            payload: new
            {
                courseId = evt.CourseId,
                courseSlug = course.CourseSlug,
                authorSlug = course.AuthorSlug,
                issueId = evt.IssueId,
                submissionId = evt.SubmissionId,
            });

        await _dispatcher.DispatchAsync(request, ct);
    }

    private async Task<string> ResolveIssueTitleAsync(Guid issueId, CancellationToken ct)
    {
        Result<IssueSearchLookupDto, Error> lookup = await _ecsClient.GetIssueSearchLookupAsync(issueId, ct);
        if (lookup.IsSuccess && lookup.Value is not null)
            return lookup.Value.Title;

        _logger.LogWarning(
            "ECS lookup failed for issue {IssueId}: {Error}. Using fallback title.",
            issueId, lookup.ErrorText());
        return "задача";
    }
}
