using AuthService.Contracts.HttpCommunication;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Database;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Domain.Subscriptions;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>education.events / issue.published</c> → подписчикам курса(ов), где опубликовано задание.
///
/// IssueTitle уже в event'е (publisher всегда его знает). CourseTitle + route-context
/// резолвятся один раз на курс через cached HTTP lookup.
/// </summary>
public sealed class IssuePublishedHandler
{
    /// <summary>
    /// См. <see cref="MaterialPublishedHandler.BATCH_SIZE"/> — та же причина.
    /// Снижено до 25 в issue #67 (prod OOM).
    /// </summary>
    private const int BATCH_SIZE = 25;
    private const int SUBSCRIBER_PAGE_SIZE = 500;

    private readonly ISubscribersQuery _subscribers;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<IssuePublishedHandler> _logger;

    public IssuePublishedHandler(
        ISubscribersQuery subscribers,
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        ILogger<IssuePublishedHandler> logger)
    {
        _subscribers = subscribers;
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(IssuePublished evt, CancellationToken ct)
    {
        if (!evt.NotifySubscribers)
        {
            _logger.LogDebug(
                "Issue {IssueId} published but NotifySubscribers=false — skipping notifications",
                evt.IssueId);
            return;
        }

        IReadOnlyList<Guid> courseIds = evt.CourseIds ?? [];
        if (courseIds.Count == 0)
            return;

        // Per-course streaming dispatch: раньше копили глобальный List<NotificationRequest> на
        // ВСЕ курсы → peak memory O(total subscribers) → OOM на большом issue. Теперь flush'им
        // chunk при заполнении → peak O(BATCH_SIZE). Зеркалит MaterialPublishedHandler (#67/#230).
        int totalDispatched = 0;
        int totalChunks = 0;
        List<NotificationRequest> chunk = new(BATCH_SIZE);

        foreach (Guid courseId in courseIds)
        {
            SubscriberPage page = await _subscribers.ByEntityPageAsync(
                SubscriptionEntityType.COURSE,
                courseId,
                afterUserId: null,
                SUBSCRIBER_PAGE_SIZE,
                ct);

            if (page.UserIds.Count == 0)
                continue;

            CourseRouteContext course = await NotificationRouteContextResolver.ResolveCourseAsync(
                _ecsClient,
                _authClient,
                courseId,
                _logger,
                ct);

            TemplateArgs args = TemplateArgs.Of(
                ("issueTitle", evt.Title),
                ("courseTitle", course.Title));

            while (true)
            {
                foreach (Guid recipient in page.UserIds)
                {
                    chunk.Add(NotificationRequest.From(
                        template: NotificationTemplates.IssuePublished,
                        recipientUserId: recipient,
                        correlationId: CorrelationIds.Combine(recipient, evt.IssueId, courseId),
                        args: args,
                        payload: new
                        {
                            courseId,
                            courseSlug = course.CourseSlug,
                            authorSlug = course.AuthorSlug,
                            issueId = evt.IssueId,
                        }));

                    if (chunk.Count >= BATCH_SIZE)
                    {
                        await _dispatcher.DispatchAsync(chunk, ct);
                        totalDispatched += chunk.Count;
                        totalChunks++;
                        chunk.Clear();
                    }
                }

                if (!page.NextAfterUserId.HasValue)
                    break;

                page = await _subscribers.ByEntityPageAsync(
                    SubscriptionEntityType.COURSE,
                    courseId,
                    page.NextAfterUserId,
                    SUBSCRIBER_PAGE_SIZE,
                    ct);
            }
        }

        if (chunk.Count > 0)
        {
            await _dispatcher.DispatchAsync(chunk, ct);
            totalDispatched += chunk.Count;
            totalChunks++;
        }

        if (totalChunks > 1)
        {
            _logger.LogInformation(
                "IssuePublished {IssueId}: dispatched {Count} notifications in {Chunks} chunks",
                evt.IssueId, totalDispatched, totalChunks);
        }
    }
}
