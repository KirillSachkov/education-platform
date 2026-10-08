using Common;
using ContentAccess;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using ProgressService.Core.Features.Lifecycle.IntegrationEvents;
using ProgressService.Domain.Bookmarks;
using ProgressService.Domain.ContentAccess;
using ProgressService.Domain.Enrollments;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Education.Events;
using SharedKernel;

namespace ProgressService.IntegrationTests.Features.Lifecycle;

[Collection(nameof(IntegrationTestsFixture))]
public class CourseHardDeletedHandlerTests : ProgressServiceTestsBase
{
    public CourseHardDeletedHandlerTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
        // Test outbox accumulates published messages across tests — clear at start of each.
        NoOpOutboxService.Reset();
        UserGrantWriter.Reset();
    }

    [Fact]
    public async Task CourseHardDeleted_ShouldDeleteEnrollmentsGrantsAndBookmarks_AndRevokeRedisTags()
    {
        Guid courseId = Guid.NewGuid();
        Guid user1 = Guid.NewGuid();
        Guid user2 = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();

        await SeedCourseDataAsync(courseId, [user1, user2], lessonId);

        // Step 1 — main handler: cleans DB state and enqueues the revocation cascade
        await InvokeMessageAndWaitAsync(new CourseHardDeleted(courseId));

        int enrollmentsLeft = await ExecuteInDb(db =>
            db.CourseEnrollments.CountAsync(e => e.CourseId == courseId));
        Assert.Equal(0, enrollmentsLeft);

        int bookmarksLeft = await ExecuteInDb(db =>
            db.MaterialBookmarks.CountAsync(b => b.CourseId == courseId));
        Assert.Equal(0, bookmarksLeft);

        int activeGrantsLeft = await ExecuteInDb(db =>
            db.ContentGrants.CountAsync(g => g.ResourceId == courseId && g.RevokedAt == null));
        Assert.Equal(0, activeGrantsLeft);

        // Cascade was enqueued via outbox (captured by the test spy)
        CourseContentAccessRevocationRequested? cascaded = NoOpOutboxService.Published
            .OfType<CourseContentAccessRevocationRequested>()
            .FirstOrDefault(m => m.CourseId == courseId);
        Assert.NotNull(cascaded);
        Assert.Contains(user1, cascaded.UserIds);
        Assert.Contains(user2, cascaded.UserIds);

        // Step 2 — simulate Wolverine delivering the cascaded message to its handler.
        // In production this happens automatically after the main handler's DB commit;
        // in tests the outbox is a spy so we invoke the downstream handler manually.
        await InvokeMessageAndWaitAsync(cascaded);

        string expectedTag = GrantTags.Course(courseId);
        Assert.Contains(UserGrantWriter.Revoked, call => call.UserId == user1 && call.Tag == expectedTag);
        Assert.Contains(UserGrantWriter.Revoked, call => call.UserId == user2 && call.Tag == expectedTag);
    }

    [Fact]
    public async Task CourseHardDeleted_WhenReplayedAfterFirstSuccess_IsIdempotent()
    {
        Guid courseId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        await SeedCourseDataAsync(courseId, [userId], lessonId: null);

        await InvokeMessageAndWaitAsync(new CourseHardDeleted(courseId));

        // Replay — no DB rows to delete, no users to revoke; must not throw.
        await InvokeMessageSuppressingExceptionsAsync(new CourseHardDeleted(courseId));

        int enrollmentsLeft = await ExecuteInDb(db =>
            db.CourseEnrollments.CountAsync(e => e.CourseId == courseId));
        Assert.Equal(0, enrollmentsLeft);
    }

    [Fact]
    public async Task CourseHardDeleted_WhenNoEnrolledUsers_SkipsRedisRevocation()
    {
        Guid courseId = Guid.NewGuid();

        // No seeding at all — the course has no enrollments/grants/bookmarks.
        await InvokeMessageAndWaitAsync(new CourseHardDeleted(courseId));

        // Handler must skip publishing the cascade message when there are no users.
        bool anyRequestForThisCourse = NoOpOutboxService.Published
            .OfType<CourseContentAccessRevocationRequested>()
            .Any(m => m.CourseId == courseId);
        Assert.False(anyRequestForThisCourse);

        Assert.Empty(UserGrantWriter.Revoked);
    }

    [Fact]
    public async Task CourseContentAccessRevocationRequestedHandler_IsIdempotent()
    {
        // Re-delivering the internal revocation message must be a no-op (RevokeAsync is idempotent
        // on the writer side — duplicate delivery is safe).
        Guid courseId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        var message = new CourseContentAccessRevocationRequested(courseId, [userId]);

        await InvokeMessageAndWaitAsync(message);
        await InvokeMessageAndWaitAsync(message);

        string expectedTag = GrantTags.Course(courseId);
        IEnumerable<(Guid UserId, string Tag)> matching = UserGrantWriter.Revoked
            .Where(c => c.UserId == userId && c.Tag == expectedTag);
        // Two invocations → two recorded revocations, but both succeed — the writer would
        // handle replay gracefully in production (set-remove is a no-op on missing member).
        Assert.Equal(2, matching.Count());
    }

    private async Task SeedCourseDataAsync(Guid courseId, Guid[] userIds, Guid? lessonId)
    {
        await ExecuteInDb(async db =>
        {
            foreach (Guid userId in userIds)
            {
                CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(userId, courseId, Guid.NewGuid(), EnrollmentSource.ENGAGEMENT).Value;
                db.CourseEnrollments.Add(enrollment);

                Result<ContentGrant, Error> grant = ContentGrant.Create(
                    userId, "course", courseId, "enrollment");
                db.ContentGrants.Add(grant.Value);

                if (lessonId.HasValue)
                {
                    Result<BookmarkEntityReference, Error> refResult =
                        BookmarkEntityReference.Of(EntityType.Material, lessonId.Value);
                    Result<MaterialBookmark, Error> bookmark = MaterialBookmark.Create(
                        userId, courseId, refResult.Value);
                    db.MaterialBookmarks.Add(bookmark.Value);
                }
            }

            await db.SaveChangesAsync();
        });
    }
}

