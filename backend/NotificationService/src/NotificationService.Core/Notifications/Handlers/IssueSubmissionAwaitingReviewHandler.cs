using AuthService.Contracts;
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
/// <c>progress.events / issue_submission.awaiting_review</c> → автору курса.
///
/// Параллельно резолвит issue title из ECS и student display name из AuthService —
/// оба вызова cached. На failure любого — graceful fallback.
/// </summary>
public sealed class IssueSubmissionAwaitingReviewHandler
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<IssueSubmissionAwaitingReviewHandler> _logger;

    public IssueSubmissionAwaitingReviewHandler(
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        ILogger<IssueSubmissionAwaitingReviewHandler> logger)
    {
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(IssueSubmissionAwaitingReview evt, CancellationToken ct)
    {
        // Параллельный fetch — оба вызова cached, но на miss идут в HTTP.
        Task<string> issueTitleTask = ResolveIssueTitleAsync(evt.IssueId, ct);
        Task<string> studentNameTask = ResolveStudentNameAsync(evt.StudentUserId, ct);
        string[] resolved = await Task.WhenAll(issueTitleTask, studentNameTask);
        string issueTitle = resolved[0];
        string studentName = resolved[1];

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.IssueSubmissionAwaitingReview,
            recipientUserId: evt.AuthorId,
            correlationId: evt.SubmissionId,
            args: TemplateArgs.Of(
                ("issueTitle", issueTitle),
                ("studentName", studentName)),
            payload: new { submissionId = evt.SubmissionId, issueId = evt.IssueId, courseId = evt.CourseId });

        await _dispatcher.DispatchAsync(request, ct);
    }

    private async Task<string> ResolveIssueTitleAsync(Guid issueId, CancellationToken ct)
    {
        Result<IssueSearchLookupDto, Error> lookup = await _ecsClient.GetIssueSearchLookupAsync(issueId, ct);
        if (lookup.IsSuccess && lookup.Value is not null)
            return lookup.Value.Title;

        _logger.LogWarning(
            "ECS lookup failed for issue {IssueId}: {Error}. Using fallback.",
            issueId, lookup.ErrorText());
        return "задача";
    }

    private async Task<string> ResolveStudentNameAsync(Guid studentUserId, CancellationToken ct)
    {
        Result<IReadOnlyList<AuthUserLookupDto>, Error> lookup =
            await _authClient.GetUsersByIdsAsync([studentUserId], ct);
        if (lookup.IsSuccess && lookup.Value is not null && lookup.Value.Count > 0)
        {
            AuthUserLookupDto user = lookup.Value[0];
            return user.Name ?? user.Username ?? "студент";
        }

        _logger.LogWarning(
            "Auth lookup failed for student {UserId}: {Error}. Using fallback.",
            studentUserId, lookup.ErrorText());
        return "студент";
    }
}
