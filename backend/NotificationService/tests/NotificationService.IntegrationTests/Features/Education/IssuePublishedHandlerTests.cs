using AuthService.Contracts.AuthorSpaces;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Notifications;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.Subscriptions;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using Shared.Messaging.IntegrationEvents.Education.Events;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.Education;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class IssuePublishedHandlerTests : NotificationServiceTestsBase
{
    public IssuePublishedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task IssuePublished_ForCourseSubscriber_CreatesIssuePublishedNotification()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        EducationContentClient.GetCourseSearchLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseSearchLookupDto, Error>(new CourseSearchLookupDto(
                Id: courseId,
                Slug: "dotnet-course",
                Title: "Dotnet Course",
                Description: "Course",
                Status: PublicationStatus.PUBLISHED,
                UpdatedAt: DateTime.UtcNow,
                RequiredAccessTags: [],
                AuthorId: authorId)));

        AuthServiceClient.GetAuthorSpaceByAuthorIdAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<AuthorSpaceRouteResponse, Error>(
                new AuthorSpaceRouteResponse(authorId, "sachkov")));

        await InvokeMessageAndWaitAsync(new UserCreated(userId, Username: "student", DisplayName: "Student"));

        await ExecuteInDb(async db =>
        {
            Subscription subscription = Subscription.Create(
                userId,
                SubscriptionEntityType.COURSE,
                courseId).Value;

            await db.Subscriptions.AddAsync(subscription);
            await db.SaveChangesAsync();
        });

        await InvokeMessageAndWaitAsync(new IssuePublished(
            IssueId: issueId,
            ProjectId: Guid.NewGuid(),
            Title: "Task 1",
            AuthorId: authorId,
            CourseIds: [courseId],
            NotifySubscribers: true));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId
                              && x.Type == NotificationType.IssuePublished));

        Assert.Equal(
            CorrelationIds.Combine(userId, issueId, courseId),
            notification.CorrelationId);
    }

    [Fact]
    public async Task IssuePublished_SecondInvocation_DoesNotCreateDuplicate()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        EducationContentClient.GetCourseSearchLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseSearchLookupDto, Error>(new CourseSearchLookupDto(
                Id: courseId,
                Slug: "dotnet-course",
                Title: "Dotnet Course",
                Description: "Course",
                Status: PublicationStatus.PUBLISHED,
                UpdatedAt: DateTime.UtcNow,
                RequiredAccessTags: [],
                AuthorId: authorId)));

        AuthServiceClient.GetAuthorSpaceByAuthorIdAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<AuthorSpaceRouteResponse, Error>(
                new AuthorSpaceRouteResponse(authorId, "sachkov")));

        await InvokeMessageAndWaitAsync(new UserCreated(userId, Username: "student", DisplayName: "Student"));

        await ExecuteInDb(async db =>
        {
            Subscription subscription = Subscription.Create(
                userId,
                SubscriptionEntityType.COURSE,
                courseId).Value;

            await db.Subscriptions.AddAsync(subscription);
            await db.SaveChangesAsync();
        });

        IssuePublished evt = new(
            IssueId: issueId,
            ProjectId: Guid.NewGuid(),
            Title: "Task 1",
            AuthorId: authorId,
            CourseIds: [courseId],
            NotifySubscribers: true);

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(x => x.RecipientUserId == userId
                             && x.Type == NotificationType.IssuePublished));

        Assert.Equal(1, count);
    }
}
