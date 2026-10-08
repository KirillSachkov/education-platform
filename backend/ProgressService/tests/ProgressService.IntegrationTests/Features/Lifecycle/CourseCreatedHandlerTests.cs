using Microsoft.EntityFrameworkCore;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace ProgressService.IntegrationTests.Features.Lifecycle;

[Collection(nameof(IntegrationTestsFixture))]
public class CourseCreatedHandlerTests : ProgressServiceTestsBase
{
    public CourseCreatedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CourseCreated_ShouldCreateActiveEnrollmentForAuthor()
    {
        // Phase E (#45): TRIAL/STANDARD distinction removed; enrollments uniform.
        Guid courseId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new CourseCreated(courseId, authorId));

        int count = await ExecuteInDb(db => db.CourseEnrollments
            .CountAsync(e => e.CourseId == courseId
                             && e.UserId == authorId
                             && e.AuthorId == authorId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CourseCreated_WhenReplayed_IsIdempotent()
    {
        Guid courseId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new CourseCreated(courseId, authorId));
        await InvokeMessageAndWaitAsync(new CourseCreated(courseId, authorId));

        int count = await ExecuteInDb(db => db.CourseEnrollments
            .CountAsync(e => e.CourseId == courseId && e.UserId == authorId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CourseCreated_WithEmptyAuthorId_SkipsEnrollment()
    {
        // Backward-compat / safety: in-flight messages from before AuthorId was added
        // deserialize to Guid.Empty and must not produce an enrollment.
        Guid courseId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new CourseCreated(courseId, Guid.Empty));

        int count = await ExecuteInDb(db => db.CourseEnrollments
            .CountAsync(e => e.CourseId == courseId));
        Assert.Equal(0, count);
    }
}
