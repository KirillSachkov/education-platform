using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Subscriptions;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using SharedKernel;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace NotificationService.IntegrationTests.Features.Education;

/// <summary>
/// Issue #80 — coverage для <c>SubscribeLifetimeGranteesOnCourseCreatedHandler</c>:
/// при <c>course.created</c> юзеры с активным LIFETIME_ALL grant'ом автора должны
/// получить subscription'ы на новый курс.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class SubscribeLifetimeGranteesOnCourseCreatedHandlerTests
    : NotificationServiceTestsBase
{
    public SubscribeLifetimeGranteesOnCourseCreatedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CourseCreated_PrePopulatesSubscriptionsForLifetimeGrantees()
    {
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid grantee1 = Guid.NewGuid();
        Guid grantee2 = Guid.NewGuid();

        AccessServiceClient
            .GetLifetimeGranteeUserIdsAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<Guid>, Error>([grantee1, grantee2]));

        await InvokeMessageAndWaitAsync(new CourseCreated(courseId, authorId));

        List<Subscription> subs = await ExecuteInDb(db => db.Subscriptions
            .AsNoTracking()
            .Where(s => s.EntityType == SubscriptionEntityType.COURSE && s.EntityId == courseId)
            .ToListAsync());

        Assert.Equal(2, subs.Count);
        Assert.Contains(subs, s => s.UserId == grantee1);
        Assert.Contains(subs, s => s.UserId == grantee2);
    }

    [Fact]
    public async Task CourseCreated_NoLifetimeGrantees_DoesNothing()
    {
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        AccessServiceClient
            .GetLifetimeGranteeUserIdsAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<Guid>, Error>([]));

        await InvokeMessageAndWaitAsync(new CourseCreated(courseId, authorId));

        int count = await ExecuteInDb(db => db.Subscriptions
            .CountAsync(s => s.EntityType == SubscriptionEntityType.COURSE && s.EntityId == courseId));

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CourseCreated_Idempotent_NoDuplicateSubscriptions()
    {
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid grantee = Guid.NewGuid();

        AccessServiceClient
            .GetLifetimeGranteeUserIdsAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<Guid>, Error>([grantee]));

        await InvokeMessageAndWaitAsync(new CourseCreated(courseId, authorId));
        await InvokeMessageAndWaitAsync(new CourseCreated(courseId, authorId));

        int count = await ExecuteInDb(db => db.Subscriptions
            .CountAsync(s => s.UserId == grantee
                          && s.EntityType == SubscriptionEntityType.COURSE
                          && s.EntityId == courseId));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CourseCreated_AccessServiceFailure_EscapesForRetry()
    {
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        AccessServiceClient
            .GetLifetimeGranteeUserIdsAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<Guid>, Error>(
                Error.Failure("access.unavailable", "Access service down")));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            InvokeMessageAndWaitAsync(new CourseCreated(courseId, authorId)));

        int count = await ExecuteInDb(db => db.Subscriptions
            .CountAsync(s => s.EntityType == SubscriptionEntityType.COURSE && s.EntityId == courseId));

        Assert.Equal(0, count);
    }
}
