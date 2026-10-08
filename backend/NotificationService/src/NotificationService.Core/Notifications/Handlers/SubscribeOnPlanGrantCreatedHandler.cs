using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Database;
using NotificationService.Domain.Subscriptions;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>access.events / plan_grant.created</c> → автоподписка пользователя на все курсы,
/// которые покрывает grant. Sibling к <see cref="PlanGrantReceivedHandler"/> на той же
/// очереди (<c>notifications.access.grant_events</c>): одно уведомление о доступе делает
/// PlanGrantReceivedHandler, fan-out subscription'ов — этот handler (single responsibility,
/// разные idempotency keys).
///
/// Derive-модель (epic access-derive-model, Phase 3): подписки теперь привязаны к grant'у,
/// а не к материализации <c>CourseEnrollment</c>'а. Раньше автоподписку делал
/// <c>CourseEnrolledHandler</c> на <c>course_enrollment.created</c>; в derive-модели
/// lazy-anchor enrollment'ы не публикуют <c>CourseEnrolled</c>, поэтому signal переехал
/// на grant (handler и событие сняты в Phase 4).
///
/// Покрытие grant'а:
/// <list type="bullet">
///   <item><c>COURSE</c> → подписка на <c>message.EffectiveCourseIds</c> (bundle, #404 — событие уже несёт их).</item>
///   <item><c>FULL_ALL / LEARN_ALL</c> → все текущие курсы платформы, через AccessService
///         <see cref="IAccessServiceClient.GetUserCoveredCoursesAsync"/> — единый источник
///         «что покрывает grant», без дублирования access-expansion в NotificationService.</item>
/// </list>
///
/// Будущие курсы lifetime-holder'ов покрываются отдельно
/// <see cref="SubscribeLifetimeGranteesOnCourseCreatedHandler"/> на <c>course.created</c>.
///
/// Идемпотентность — batch ensure-subscription через
/// одним запросом существующих course id (ре-deliver того же grant'а не плодит дубликатов).
/// Ошибки внешнего lookup'а и сохранения выходят в Wolverine retry: sibling handlers
/// идемпотентны по correlation id, поэтому повтор envelope безопасен.
/// </summary>
public sealed class SubscribeOnPlanGrantCreatedHandler
{
    private readonly IAccessServiceClient _accessClient;
    private readonly ISubscriptionsRepository _subscriptions;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<SubscribeOnPlanGrantCreatedHandler> _logger;

    public SubscribeOnPlanGrantCreatedHandler(
        IAccessServiceClient accessClient,
        ISubscriptionsRepository subscriptions,
        ITransactionManager transactions,
        ILogger<SubscribeOnPlanGrantCreatedHandler> logger)
    {
        _accessClient = accessClient;
        _subscriptions = subscriptions;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task Handle(PlanGrantCreated evt, CancellationToken ct)
    {
        await SubscribeToCoveredCoursesAsync(evt, ct);
    }

    private async Task SubscribeToCoveredCoursesAsync(PlanGrantCreated evt, CancellationToken ct)
    {
        IReadOnlyList<Guid> courseIds = await ResolveCoveredCoursesAsync(evt, ct);
        if (courseIds.Count == 0)
            return;

        Guid[] distinctCourseIds = courseIds
            .Where(static courseId => courseId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (distinctCourseIds.Length == 0)
            return;

        IReadOnlyList<Subscription> existing = await _subscriptions.ListBy(
            subscription => subscription.UserId == evt.UserId
                && subscription.EntityType == SubscriptionEntityType.COURSE
                && distinctCourseIds.Contains(subscription.EntityId),
            ct);
        HashSet<Guid> existingCourseIds = existing
            .Select(static subscription => subscription.EntityId)
            .ToHashSet();

        var newSubscriptions = new List<Subscription>(distinctCourseIds.Length - existingCourseIds.Count);
        foreach (Guid courseId in distinctCourseIds)
        {
            if (existingCourseIds.Contains(courseId))
                continue;

            Result<Subscription, Error> created = Subscription.Create(
                evt.UserId, SubscriptionEntityType.COURSE, courseId);
            if (created.IsFailure)
                continue;

            newSubscriptions.Add(created.Value);
        }

        if (newSubscriptions.Count == 0)
            return;

        await _subscriptions.AddRangeAsync(newSubscriptions, ct);
        UnitResult<Error> saved = await _transactions.SaveChangesAsync(ct);
        if (saved.IsFailure)
        {
            _logger.LogError(
                "Failed to create {Count} plan-grant course subscription(s) for {UserId}: {Error}",
                newSubscriptions.Count, evt.UserId, saved.Error.Messages[0].Message);
            throw saved.Error.AsTransient().ToException();
        }
    }

    private async Task<IReadOnlyList<Guid>> ResolveCoveredCoursesAsync(
        PlanGrantCreated evt,
        CancellationToken ct)
    {
        // COURSE grant — событие уже несёт точные courseId'ы (bundle, #404), ECS/AccessService-хоп
        // не нужен. EffectiveCourseIds предпочитает новый CourseIds-список, падает на legacy CourseId.
        if (string.Equals(evt.PlanTier, PlanTierNames.COURSE, StringComparison.Ordinal))
        {
            IReadOnlyList<Guid> courseIds = evt.EffectiveCourseIds
                .Where(id => id != Guid.Empty)
                .ToList();
            if (courseIds.Count > 0)
                return courseIds;

            _logger.LogWarning(
                "PlanGrantCreated {GrantId} tier=COURSE has no CourseIds — no subscription created",
                evt.GrantId);
            return [];
        }

        // FULL_ALL / LEARN_ALL — резолвим текущие курсы платформы через AccessService
        // covered-courses без author-фильтра, единый источник покрытия grant'а.
        if (string.Equals(evt.PlanTier, PlanTierNames.FULL_ALL, StringComparison.Ordinal)
            || string.Equals(evt.PlanTier, PlanTierNames.LEARN_ALL, StringComparison.Ordinal))
        {
            Result<CoveredCoursesResult, Error> covered = await _accessClient
                .GetUserCoveredCoursesAsync(evt.UserId, authorId: null, ct);

            if (covered.IsFailure)
            {
                _logger.LogError(
                    "Failed to resolve global covered courses for grant {GrantId} (user {UserId}): {Error}",
                    evt.GrantId, evt.UserId,
                    covered.Error.Messages is { Count: > 0 } msgs ? msgs[0].Message : "unknown");
                throw covered.Error.ToException();
            }

            return covered.Value?.CourseIds ?? [];
        }

        // SUBSCRIPTION / прочие тиры — нет course-scope для подписки.
        _logger.LogDebug(
            "PlanGrantCreated {GrantId} tier={Tier} — no course subscription fan-out",
            evt.GrantId, evt.PlanTier);
        return [];
    }
}
