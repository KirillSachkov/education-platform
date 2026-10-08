using AuthService.Contracts;
using AuthService.Contracts.AuthorSpaces;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using Shared.Messaging.IntegrationEvents.Progress.Events;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для submission-event'ов (issue #230 TEST-2):
/// approved / changes_requested / awaiting_review.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class IssueSubmissionHandlerTests : NotificationServiceTestsBase
{
    public IssueSubmissionHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task IssueSubmissionApproved_CreatesNotificationForStudent()
    {
        Guid studentId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid submissionId = Guid.NewGuid();

        StubIssueLookup(issueId, "Решить задачу X");
        StubCourseLookup(courseId, authorId, "dotnet-course");

        await InvokeMessageAndWaitAsync(new IssueSubmissionApproved(
            SubmissionId: submissionId,
            UserId: studentId,
            IssueId: issueId,
            CourseId: courseId,
            ReviewerId: Guid.NewGuid(),
            Comment: "Хорошая работа",
            ReviewedAt: DateTimeOffset.UtcNow));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == studentId));

        Assert.Equal(NotificationType.IssueSubmissionApproved, notification.Type);
        Assert.Equal(submissionId, notification.CorrelationId);
        Assert.Contains("Решить задачу X", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IssueSubmissionApproved_Twice_IsIdempotent()
    {
        Guid studentId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid submissionId = Guid.NewGuid();

        StubIssueLookup(issueId, "Задача");
        StubCourseLookup(courseId, authorId, "course");

        IssueSubmissionApproved evt = new(
            SubmissionId: submissionId,
            UserId: studentId,
            IssueId: issueId,
            CourseId: courseId,
            ReviewerId: Guid.NewGuid(),
            Comment: null,
            ReviewedAt: DateTimeOffset.UtcNow);

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == studentId));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task IssueSubmissionChangesRequested_CreatesNotificationForStudent()
    {
        Guid studentId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid submissionId = Guid.NewGuid();

        StubIssueLookup(issueId, "Реализовать middleware");
        StubCourseLookup(courseId, authorId, "course");

        await InvokeMessageAndWaitAsync(new IssueSubmissionChangesRequested(
            SubmissionId: submissionId,
            UserId: studentId,
            IssueId: issueId,
            CourseId: courseId,
            ReviewerId: Guid.NewGuid(),
            Comment: "Поправь именование",
            ReviewedAt: DateTimeOffset.UtcNow));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == studentId));

        Assert.Equal(NotificationType.IssueSubmissionChangesRequested, notification.Type);
        Assert.Equal(submissionId, notification.CorrelationId);
    }

    [Fact]
    public async Task IssueSubmissionAwaitingReview_NotifiesAuthor_NotStudent()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid submissionId = Guid.NewGuid();

        StubIssueLookup(issueId, "PR review");
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(c => c.Contains(studentId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(studentId, "Иван Иванов", "ivan", "ivan@e.com", null)]));

        await InvokeMessageAndWaitAsync(new IssueSubmissionAwaitingReview(
            SubmissionId: submissionId,
            StudentUserId: studentId,
            AuthorId: authorId,
            IssueId: issueId,
            CourseId: courseId,
            SubmittedAt: DateTimeOffset.UtcNow,
            Payload: "https://github.com/org/repo/pull/42"));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(NotificationType.IssueSubmissionAwaitingReview, notification.Type);
        Assert.Equal(submissionId, notification.CorrelationId);
        Assert.Contains("Иван Иванов", notification.Body, StringComparison.Ordinal);
        Assert.Equal(NotificationChannel.None, notification.Channels & NotificationChannel.Telegram);

        NotificationCreated published = OutboxCollector.OfType<NotificationCreated>()
            .Single(x => x.RecipientUserId == authorId);
        Assert.Null(published.TelegramBody);

        int studentSideCount = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == studentId));
        Assert.Equal(0, studentSideCount);
    }

    private void StubIssueLookup(Guid issueId, string title)
    {
        EducationContentClient.GetIssueSearchLookupAsync(issueId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IssueSearchLookupDto, Error>(new IssueSearchLookupDto(
                Id: issueId,
                ProjectId: Guid.NewGuid(),
                CourseId: null,
                CourseSlug: null,
                Title: title,
                ModuleId: null,
                CourseTitle: null,
                ProjectTitle: null,
                ModuleTitle: null,
                Status: PublicationStatus.PUBLISHED,
                RequiredAccessTags: [],
                UpdatedAt: DateTime.UtcNow)));
    }

    private void StubCourseLookup(Guid courseId, Guid authorId, string slug)
    {
        EducationContentClient.GetCourseSearchLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseSearchLookupDto, Error>(new CourseSearchLookupDto(
                Id: courseId,
                Slug: slug,
                Title: "Course",
                Description: "x",
                Status: PublicationStatus.PUBLISHED,
                UpdatedAt: DateTime.UtcNow,
                RequiredAccessTags: [],
                AuthorId: authorId)));

        AuthServiceClient.GetAuthorSpaceByAuthorIdAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<AuthorSpaceRouteResponse, Error>(
                new AuthorSpaceRouteResponse(authorId, "sachkov")));
    }
}
