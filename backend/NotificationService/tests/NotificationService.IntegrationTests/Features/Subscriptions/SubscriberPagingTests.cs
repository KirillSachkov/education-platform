using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Database;
using NotificationService.Domain.Subscriptions;
using NotificationService.Infrastructure.Postgres;
using NotificationService.Infrastructure.Postgres.Configurations;
using NotificationService.IntegrationTests.Infrastructure;

namespace NotificationService.IntegrationTests.Features.Subscriptions;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class SubscriberPagingTests : NotificationServiceTestsBase
{
    public SubscriberPagingTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ByEntityPage_ReturnsStableBoundedPages_AndCount()
    {
        Guid courseId = Guid.NewGuid();
        Guid unrelatedCourseId = Guid.NewGuid();
        Guid[] expectedUserIds = Enumerable.Range(0, 57)
            .Select(_ => Guid.NewGuid())
            .Order()
            .ToArray();

        await ExecuteInDb(async db =>
        {
            foreach (Guid userId in expectedUserIds)
            {
                await db.Subscriptions.AddAsync(
                    Subscription.Create(userId, SubscriptionEntityType.COURSE, courseId).Value);
            }

            await db.Subscriptions.AddAsync(
                Subscription.Create(Guid.NewGuid(), SubscriptionEntityType.COURSE, unrelatedCourseId).Value);
            await db.Subscriptions.AddAsync(
                Subscription.Create(Guid.NewGuid(), SubscriptionEntityType.AUTHOR, courseId).Value);
            await db.SaveChangesAsync();
        });

        (int count, List<Guid> actualUserIds, List<int> pageSizes, string indexDefinition) =
            await ExecuteInDb(async db =>
        {
            ISubscribersQuery query = new SubscriptionsRepository(db);
            int totalCount = await query.CountByEntityAsync(
                SubscriptionEntityType.COURSE,
                courseId);

            List<Guid> userIds = [];
            List<int> sizes = [];
            Guid? afterUserId = null;

            do
            {
                SubscriberPage page = await query.ByEntityPageAsync(
                    SubscriptionEntityType.COURSE,
                    courseId,
                    afterUserId,
                    limit: 20);

                userIds.AddRange(page.UserIds);
                sizes.Add(page.UserIds.Count);
                afterUserId = page.NextAfterUserId;
            }
            while (afterUserId.HasValue);

            string indexDefinition = await db.Database.SqlQuery<string>($"""
                    SELECT indexdef AS "Value"
                    FROM pg_indexes
                    WHERE schemaname = {"notifications"}
                      AND indexname = {SubscriptionsIndex.ENTITY_USER}
                    """)
                .SingleAsync();

            return (totalCount, userIds, sizes, indexDefinition);
        });

        Assert.Equal(57, count);
        Assert.Equal([20, 20, 17], pageSizes);
        Assert.Equal(expectedUserIds, actualUserIds);
        Assert.Contains(
            "(entity_type, entity_id, user_id)",
            indexDefinition,
            StringComparison.Ordinal);

        await Assert.ThrowsAsync<DbUpdateException>(() => ExecuteInDb(async db =>
        {
            await db.Subscriptions.AddAsync(
                Subscription.Create(expectedUserIds[0], SubscriptionEntityType.COURSE, courseId).Value);
            await db.SaveChangesAsync();
        }));
    }
}
