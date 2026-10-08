using AccessService.Contracts.PlanGrants.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.Subscriptions;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharedKernel;
using Shared.Messaging.IntegrationEvents.Access.Events;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для <c>SubscribeOnPlanGrantCreatedHandler</c> (epic
/// access-derive-model, Phase 3.1). Автоподписка на курсы переехала с
/// <c>course_enrollment.created</c> на <c>plan_grant.created</c>:
/// COURSE → подписка на event.CourseId; FULL_ALL/LEARN_ALL → подписки на все
/// текущие курсы платформы (через AccessService covered-courses); идемпотентно.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class SubscribeOnPlanGrantCreatedHandlerTests : NotificationServiceTestsBase
{
    public SubscribeOnPlanGrantCreatedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task PlanGrantCreated_Course_SubscribesToThatCourse()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(BuildEvent(
            userId,
            planTier: "COURSE",
            courseId: courseId,
            planAuthorId: Guid.NewGuid()));

        List<Subscription> subs = await ExecuteInDb(db => db.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId
                        && s.EntityType == SubscriptionEntityType.COURSE)
            .ToListAsync());

        Subscription sub = Assert.Single(subs);
        Assert.Equal(courseId, sub.EntityId);
    }

    [Fact]
    public async Task PlanGrantCreated_FullAll_SubscribesToAllCurrentPlatformCourses()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid course1 = Guid.NewGuid();
        Guid course2 = Guid.NewGuid();

        AccessServiceClient
            .GetUserCoveredCoursesAsync(userId, null, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CoveredCoursesResult, Error>(
                new CoveredCoursesResult([course1, course2])));

        // Use the shared wire-contract tier constant (not a bare literal): if the PlanTier enum
        // member name drifts but PlanTierNames is updated to match, the publisher emits the same
        // string the handler keys on. The AccessService PlanTierNamesDriftTests guards the link.
        await InvokeMessageAndWaitAsync(BuildEvent(
            userId,
            planTier: PlanTierNames.FULL_ALL,
            courseId: null,
            planAuthorId: authorId));

        List<Subscription> subs = await ExecuteInDb(db => db.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId
                        && s.EntityType == SubscriptionEntityType.COURSE)
            .ToListAsync());

        Assert.Equal(2, subs.Count);
        Assert.Contains(subs, s => s.EntityId == course1);
        Assert.Contains(subs, s => s.EntityId == course2);
    }

    [Fact]
    public async Task PlanGrantCreated_LearnAll_UsesCoveredCoursesExpansion()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        AccessServiceClient
            .GetUserCoveredCoursesAsync(userId, null, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CoveredCoursesResult, Error>(
                new CoveredCoursesResult([courseId])));

        await InvokeMessageAndWaitAsync(BuildEvent(
            userId,
            planTier: "LEARN_ALL",
            courseId: null,
            planAuthorId: authorId));

        bool subscribed = await ExecuteInDb(db => db.Subscriptions
            .AnyAsync(s => s.UserId == userId
                           && s.EntityType == SubscriptionEntityType.COURSE
                           && s.EntityId == courseId));

        Assert.True(subscribed);
    }

    [Fact]
    public async Task PlanGrantCreated_Course_Twice_IsIdempotent_NoDuplicateSubscription()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        PlanGrantCreated evt = BuildEvent(
            userId,
            planTier: "COURSE",
            courseId: courseId,
            planAuthorId: Guid.NewGuid());

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Subscriptions
            .CountAsync(s => s.UserId == userId
                             && s.EntityType == SubscriptionEntityType.COURSE
                             && s.EntityId == courseId));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PlanGrantCreated_AccessServiceFailure_EscapesForRetry()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        AccessServiceClient
            .GetUserCoveredCoursesAsync(userId, null, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CoveredCoursesResult, Error>(
                Error.Failure("access.unavailable", "Access service down")));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            InvokeMessageAndWaitAsync(BuildEvent(
                userId,
                planTier: "FULL_ALL",
                courseId: null,
                planAuthorId: authorId)));

        int count = await ExecuteInDb(db => db.Subscriptions
            .CountAsync(s => s.UserId == userId
                             && s.EntityType == SubscriptionEntityType.COURSE));

        Assert.Equal(0, count);
    }

    /// <summary>
    /// Регрессия #444: covered-courses БРОСАЕТ исключение (как делал пустой
    /// <c>AccessServiceOptions.Url</c> через <c>UriFormatException</c> в проде — не
    /// <c>Result.Failure</c>, а throw на построении клиента). Ошибка должна выйти
    /// в Wolverine retry; sibling-handler'ы идемпотентны по correlation id.
    /// </summary>
    [Fact]
    public async Task PlanGrantCreated_CoveredCoursesThrows_EscapesForRetry()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        AccessServiceClient
            .GetUserCoveredCoursesAsync(userId, null, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("covered-courses boom"));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            InvokeMessageAndWaitAsync(BuildEvent(
                userId,
                planTier: PlanTierNames.FULL_ALL,
                courseId: null,
                planAuthorId: authorId)));

        // Подписки не созданы; повтор события восстановит fan-out после AccessService recovery.
        int subscriptions = await ExecuteInDb(db => db.Subscriptions
            .CountAsync(s => s.UserId == userId && s.EntityType == SubscriptionEntityType.COURSE));
        Assert.Equal(0, subscriptions);
    }

    /// <summary>
    /// End-to-end: после grant'а на курс подписка существует → последующий
    /// <c>material.published</c> на этот курс доходит до подписанного студента
    /// (subscription → notification path). Доказывает, что Phase 3 не оставил
    /// subscription gap: notifications полностью grant-driven.
    /// </summary>
    [Fact]
    public async Task PlanGrantCreated_ThenMaterialPublished_ReachesSubscribedStudent()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(BuildEvent(
            userId,
            planTier: "COURSE",
            courseId: courseId,
            planAuthorId: Guid.NewGuid()));

        await InvokeMessageAndWaitAsync(new MaterialPublished(
            MaterialId: materialId,
            Title: "Новый материал",
            AuthorId: Guid.NewGuid(),
            CourseIds: [courseId],
            NotifySubscribers: true));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(n => n.RecipientUserId == userId
                              && n.Type == NotificationType.MaterialPublished));

        Assert.Contains("Новый материал", notification.Body, StringComparison.Ordinal);
    }

    private static PlanGrantCreated BuildEvent(
        Guid userId,
        string planTier,
        Guid? courseId,
        Guid planAuthorId) =>
        new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: Guid.NewGuid(),
            PlanTier: planTier,
            PlanAuthorId: planAuthorId,
            CourseId: courseId,
            IncludesFutureContent: planTier is "FULL_ALL" or "LEARN_ALL",
            Source: "ADMIN_GRANT",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null);
}
