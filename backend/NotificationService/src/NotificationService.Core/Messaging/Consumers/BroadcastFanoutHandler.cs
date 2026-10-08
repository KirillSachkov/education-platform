using AuthService.Contracts.HttpCommunication;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Database;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Notifications;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.Subscriptions;
using Shared.Messaging.IntegrationEvents.Notifications.Events;

namespace NotificationService.Core.Messaging.Consumers;

/// <summary>
/// Разворачивает <see cref="NotificationBroadcastRequested"/> в N <see cref="NotificationRequest"/>
/// — по одному на подписчика — и передаёт их обычному <see cref="INotificationDispatcher"/>,
/// чтобы broadcast шёл через стандартные user-channels и идемпотентность.
///
/// Идемпотентность: <c>CorrelationId = broadcastId XOR recipientUserId</c>. Повтор того же
/// broadcast'а не создаст дублей, но разные recipient'ы не блокируют друг друга.
/// </summary>
public sealed class BroadcastFanoutHandler
{
    /// <summary>
    /// См. <see cref="MaterialPublishedHandler.BATCH_SIZE"/>. Снижено до 25 в issue #67 (prod OOM).
    /// </summary>
    private const int BATCH_SIZE = 25;
    private const int SUBSCRIBER_PAGE_SIZE = 500;

    private readonly ISubscribersQuery _subscribers;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<BroadcastFanoutHandler> _logger;

    public BroadcastFanoutHandler(
        ISubscribersQuery subscribers,
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        ILogger<BroadcastFanoutHandler> logger)
    {
        _subscribers = subscribers;
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(NotificationBroadcastRequested evt, CancellationToken ct)
    {
        SubscriberPage page = await _subscribers.ByEntityPageAsync(
            evt.TargetType,
            evt.TargetId,
            afterUserId: null,
            SUBSCRIBER_PAGE_SIZE,
            ct);

        if (page.UserIds.Count == 0)
        {
            _logger.LogInformation(
                "Broadcast {BroadcastId}: no subscribers for {TargetType}:{TargetId}",
                evt.BroadcastId, evt.TargetType, evt.TargetId);
            return;
        }

        TemplateArgs args = TemplateArgs.Of(
            ("announcementTitle", evt.Title),
            ("announcementBody", evt.Body));

        NotificationChannel? channelsOverride = evt.Channels == 0
            ? null
            : (NotificationChannel)evt.Channels;

        object payload = await BuildPayloadAsync(evt, ct);
        int totalDispatched = 0;

        while (true)
        {
            foreach (Guid[] recipients in page.UserIds.Chunk(BATCH_SIZE))
            {
                List<NotificationRequest> batch = new(recipients.Length);
                foreach (Guid recipient in recipients)
                {
                    batch.Add(NotificationRequest.From(
                        template: NotificationTemplates.AuthorAnnouncement,
                        recipientUserId: recipient,
                        correlationId: CorrelationIds.Combine(evt.BroadcastId, recipient),
                        args: args,
                        payload: payload,
                        channelsOverride: channelsOverride));
                }

                await _dispatcher.DispatchAsync(batch, ct);
                totalDispatched += batch.Count;
            }

            if (!page.NextAfterUserId.HasValue)
                break;

            page = await _subscribers.ByEntityPageAsync(
                evt.TargetType,
                evt.TargetId,
                page.NextAfterUserId,
                SUBSCRIBER_PAGE_SIZE,
                ct);
        }

        _logger.LogInformation(
            "Broadcast {BroadcastId} dispatched to {Count} recipients",
            evt.BroadcastId, totalDispatched);
    }

    private async Task<object> BuildPayloadAsync(NotificationBroadcastRequested evt, CancellationToken ct)
    {
        if (string.Equals(evt.TargetType, SubscriptionEntityType.COURSE, StringComparison.Ordinal))
        {
            CourseRouteContext course = await NotificationRouteContextResolver.ResolveCourseAsync(
                _ecsClient,
                _authClient,
                evt.TargetId,
                _logger,
                ct);

            return new
            {
                broadcastId = evt.BroadcastId,
                targetType = evt.TargetType,
                targetId = evt.TargetId,
                courseSlug = course.CourseSlug,
                authorSlug = course.AuthorSlug,
            };
        }

        if (string.Equals(evt.TargetType, SubscriptionEntityType.AUTHOR, StringComparison.Ordinal))
        {
            string? authorSlug = await NotificationRouteContextResolver.ResolveAuthorSlugAsync(
                _authClient,
                evt.TargetId,
                _logger,
                ct);

            return new
            {
                broadcastId = evt.BroadcastId,
                targetType = evt.TargetType,
                targetId = evt.TargetId,
                authorSlug,
            };
        }

        return new
        {
            broadcastId = evt.BroadcastId,
            targetType = evt.TargetType,
            targetId = evt.TargetId,
        };
    }
}
