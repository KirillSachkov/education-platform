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
/// <c>education.events / material.published</c> → подписчикам курса(ов), где опубликован материал.
///
/// MaterialTitle уже в event'е (publisher всегда его знает). CourseTitle + route-context
/// резолвятся один раз на курс; если материал опубликован в нескольких курсах, уведомления
/// строятся с корректной ссылкой для каждого курса.
/// </summary>
public sealed class MaterialPublishedHandler
{
    /// <summary>
    /// Размер чанка для <see cref="INotificationDispatcher.DispatchAsync"/>. Внутри
    /// dispatcher'а для каждого request'а — отдельная транзакция с outbox publish.
    /// На большом курсе (1000+ enrollments) один большой батч превращался в одну
    /// многоминутную handler-таску → handler timeout → Wolverine retry → дубль.
    ///
    /// Снижено с 200 до 25 после prod-инцидента 2026-05-06 (issue #67): даже при
    /// per-notification transactions память NS hit'ала 512M cgroup limit и cgroup
    /// OOM-kill'ил dotnet. EF ChangeTracker + Wolverine outbox buffer + email
    /// recipient cache pre-warm удерживают entities в scoped DbContext'е до конца
    /// handler-таски — не до конца отдельной транзакции. 25 ограничивает peak
    /// memory pressure при сохранении хорошей пропускной способности.
    /// </summary>
    private const int BATCH_SIZE = 25;
    private const int SUBSCRIBER_PAGE_SIZE = 500;

    private readonly ISubscribersQuery _subscribers;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<MaterialPublishedHandler> _logger;

    public MaterialPublishedHandler(
        ISubscribersQuery subscribers,
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        ILogger<MaterialPublishedHandler> logger)
    {
        _subscribers = subscribers;
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(MaterialPublished evt, CancellationToken ct)
    {
        if (!evt.NotifySubscribers)
        {
            _logger.LogDebug(
                "Material {MaterialId} published but NotifySubscribers=false — skipping notifications",
                evt.MaterialId);
            return;
        }

        if (evt.CourseIds.Count == 0)
            return;

        // Per-course dispatch: раньше копили глобальный List<NotificationRequest> на все
        // курсы → peak memory = O(total). Теперь — O(subscribers_per_course) (issue #230 MSG-2).
        int totalDispatched = 0;
        int totalChunks = 0;
        List<NotificationRequest> chunk = new(BATCH_SIZE);

        foreach (Guid courseId in evt.CourseIds)
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
                ("materialTitle", evt.Title),
                ("courseTitle", course.Title));

            while (true)
            {
                foreach (Guid recipient in page.UserIds)
                {
                    chunk.Add(NotificationRequest.From(
                        template: NotificationTemplates.MaterialPublished,
                        recipientUserId: recipient,
                        correlationId: CorrelationIds.Combine(recipient, evt.MaterialId, courseId),
                        args: args,
                        payload: new
                        {
                            courseId,
                            courseSlug = course.CourseSlug,
                            authorSlug = course.AuthorSlug,
                            materialId = evt.MaterialId,
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
                "MaterialPublished {MaterialId}: dispatched {Count} notifications in {Chunks} chunks",
                evt.MaterialId, totalDispatched, totalChunks);
        }
    }
}
