using AccessService.Contracts.HttpCommunication;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Database;
using NotificationService.Domain.Subscriptions;
using SharedKernel;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// Slot subscriber pre-population для FULL_ALL / LEARN_ALL grant'ов (issue #80).
///
/// Когда появляется НОВЫЙ курс, юзеры с активным <c>plan:all</c> grant'ом
/// автомагически получают доступ (через Redis ∩). Но subscription'ы
/// создаются <see cref="CourseEnrolledHandler"/>'ом только при материализации
/// enrollment'а — для старых grant'ов на новый курс enrollment всё-таки создаётся
/// (через ProgressService.PlanGrantCreatedHandler... только при выдаче grant'а,
/// не при появлении курса).
///
/// Этот handler закрывает gap: на <c>course.created</c> запрашивает у AccessService
/// список global FULL/LEARN-grantee'ов и идемпотентно создаёт subscription для
/// каждого. Без этого `material.published` для нового курса не дойдёт до full-access
/// owner'ов.
/// </summary>
public sealed class SubscribeLifetimeGranteesOnCourseCreatedHandler
{
    private readonly IAccessServiceClient _accessClient;
    private readonly ISubscriptionsRepository _subscriptions;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<SubscribeLifetimeGranteesOnCourseCreatedHandler> _logger;

    public SubscribeLifetimeGranteesOnCourseCreatedHandler(
        IAccessServiceClient accessClient,
        ISubscriptionsRepository subscriptions,
        ITransactionManager transactions,
        ILogger<SubscribeLifetimeGranteesOnCourseCreatedHandler> logger)
    {
        _accessClient = accessClient;
        _subscriptions = subscriptions;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task Handle(CourseCreated message, CancellationToken cancellationToken)
    {
        if (message.AuthorId == Guid.Empty)
        {
            _logger.LogWarning(
                "CourseCreated {CourseId} has empty AuthorId — skipping lifetime-grantee subscribe",
                message.CourseId);
            return;
        }

        Result<IReadOnlyList<Guid>, Error> grantees = await _accessClient
            .GetLifetimeGranteeUserIdsAsync(message.AuthorId, cancellationToken);

        if (grantees.IsFailure)
        {
            _logger.LogWarning(
                "Failed to query lifetime grantees for author {AuthorId} (course {CourseId}): {Error}",
                message.AuthorId, message.CourseId,
                grantees.Error.Messages is { Count: > 0 } _msgs ? _msgs[0].Message : "unknown");

            throw grantees.Error.ToException();
        }

        IReadOnlyList<Guid> userIds = grantees.Value;
        if (userIds.Count == 0)
        {
            _logger.LogDebug(
                "CourseCreated {CourseId}: no global full-access grantees — no subscriptions created",
                message.CourseId);
            return;
        }

        // Batch-проверка существующих подписок — один запрос вместо N ExistsBy
        // (issue #230, PERF-1). Для популярного автора с 500+ grantee'ями это
        // 500 round-trip'ов превратятся в 1.
        IReadOnlySet<Guid> alreadySubscribed = await _subscriptions.GetSubscribedUserIdsAsync(
            userIds,
            SubscriptionEntityType.COURSE,
            message.CourseId,
            cancellationToken);

        List<Subscription> toAdd = new(userIds.Count - alreadySubscribed.Count);
        int createFailures = 0;
        string? firstCreateError = null;

        foreach (Guid userId in userIds)
        {
            if (alreadySubscribed.Contains(userId))
                continue;

            Result<Subscription, Error> result = Subscription.Create(
                userId, SubscriptionEntityType.COURSE, message.CourseId);
            if (result.IsFailure)
            {
                createFailures++;
                firstCreateError ??=
                    result.Error.Messages is { Count: > 0 } msgs ? msgs[0].Message : "unknown";
                continue;
            }

            toAdd.Add(result.Value);
        }

        if (createFailures > 0)
        {
            // Один агрегированный warning вместо per-grantee burst'а (issue #230, LOG-2).
            _logger.LogWarning(
                "Subscription.Create failed for {Failures} grantee(s) of course {CourseId}: {SampleError}",
                createFailures, message.CourseId, firstCreateError);
        }

        if (toAdd.Count == 0)
        {
            _logger.LogInformation(
                "CourseCreated {CourseId}: all {Total} global full-access grantees already subscribed",
                message.CourseId, userIds.Count);
            return;
        }

        await _subscriptions.AddRangeAsync(toAdd, cancellationToken);

        UnitResult<Error> saved = await _transactions.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            _logger.LogError(
                "Failed to save {Count} global full-access subscriptions for course {CourseId}: {Error}",
                toAdd.Count, message.CourseId,
                saved.Error.Messages is { Count: > 0 } msgs ? msgs[0].Message : "unknown");

            throw saved.Error.AsTransient().ToException();
        }

        _logger.LogInformation(
            "CourseCreated {CourseId}: pre-populated {Created} global full-access subscription(s) (of {Total} grantees)",
            message.CourseId, toAdd.Count, userIds.Count);
    }
}
