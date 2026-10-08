using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using SharedKernel;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>assignment_review.events / ai_review.oversized_skipped</c> → автору курса (#546).
///
/// Авто-ран AI-проверки пропущен: reviewable diff PR'а выше hard-cap'ов ARS. Автор —
/// единственный, кто может запустить проверку вручную («Перепроверить» снимает cap и
/// ревьюит PR целиком по частям), поэтому сигналим именно ему. ARS дедупит событие
/// (один раз на AiReview), correlation = AiReviewId — второй guard на retry.
/// Issue title из ECS и student display name из AuthService резолвятся параллельно —
/// оба вызова cached, на failure любого — graceful fallback. Линк ведёт на author
/// review-страницу (через <c>PlatformLinkBuilder</c>).
/// </summary>
public sealed class AiReviewOversizedSkippedHandler
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<AiReviewOversizedSkippedHandler> _logger;

    public AiReviewOversizedSkippedHandler(
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        ILogger<AiReviewOversizedSkippedHandler> logger)
    {
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(AiReviewOversizedSkipped evt, CancellationToken ct)
    {
        // Параллельный fetch — оба вызова cached, но на miss идут в HTTP.
        Task<string> issueTitleTask = ResolveIssueTitleAsync(evt.IssueId, ct);
        Task<string> studentNameTask = ResolveStudentNameAsync(evt.StudentUserId, ct);
        string[] resolved = await Task.WhenAll(issueTitleTask, studentNameTask);
        string issueTitle = resolved[0];
        string studentName = resolved[1];

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.AiReviewOversizedSkipped,
            recipientUserId: evt.AuthorId,
            correlationId: evt.AiReviewId,
            args: TemplateArgs.Of(
                ("issueTitle", issueTitle),
                ("studentName", studentName),
                ("pullRequest", $"{evt.RepoFullName}#{evt.PullNumber}")),
            payload: new { submissionId = evt.SubmissionId, issueId = evt.IssueId, aiReviewId = evt.AiReviewId });

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
